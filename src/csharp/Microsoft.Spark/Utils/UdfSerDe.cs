// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;

namespace Microsoft.Spark.Utils
{
    /// <summary>
    /// UdfSerDe is responsible for serializing/deserializing an UDF.
    /// </summary>
    internal class UdfSerDe
    {
        [Serializable]
        internal sealed class UdfData
        {
            public Type TypeData { get; set; }
            public string MethodName { get; set; }
            public TargetData TargetData { get; set; }

            public override int GetHashCode()
            {
                return base.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                return (obj is UdfData udfData) &&
                    Equals(udfData);
            }

            public bool Equals(UdfData other)
            {
                return (other != null) &&
                    TypeData.Equals(other.TypeData) &&
                    (MethodName == other.MethodName) &&
                    TargetData.Equals(other.TargetData);
            }
        }

        [Serializable]
        internal sealed class TargetData
        {
            public Type TypeData { get; set; }
            public FieldData[] Fields { get; set; }

            public override int GetHashCode()
            {
                return base.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                return (obj is TargetData targetData) &&
                    Equals(targetData);
            }

            public bool Equals(TargetData other)
            {
                if ((other == null) ||
                    !TypeData.Equals(other.TypeData) ||
                    (Fields?.Length != other.Fields?.Length))
                {
                    return false;
                }

                if ((Fields == null) && (other.Fields == null))
                {
                    return true;
                }

                return Fields.SequenceEqual(other.Fields);
            }
        }

        [Serializable]
        internal sealed class FieldData
        {
            public Type TypeData { get; set; }
            public string Name { get; set; }
            public object Value { get; set; }

            public override int GetHashCode()
            {
                return base.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                return (obj is FieldData fieldData) &&
                    Equals(fieldData);
            }

            public bool Equals(FieldData other)
            {
                return (other != null) &&
                    TypeData.Equals(other.TypeData) &&
                    (Name == other.Name) &&
                    (((Value == null) && (other.Value == null)) ||
                        ((Value != null) && Value.Equals(other.Value)));
            }
        }

        internal static UdfData Serialize(Delegate udf)
        {
            MethodInfo method = udf.Method;
            object target = udf.Target;

            var udfData = new UdfData()
            {
                TypeData = method.DeclaringType,
                MethodName = method.Name,
                TargetData = SerializeTarget(target)
            };

            return udfData;
        }

        internal static Delegate Deserialize(UdfData udfData)
        {
            Console.Error.WriteLine("Deserialize UdfData (TypeData = {0}; MethodName = {1}; Target = {2})", udfData.TypeData, udfData.MethodName, udfData.TargetData);

            Type udfType = udfData.TypeData;
            MethodInfo udfMethod = udfType.GetMethod(
                udfData.MethodName,
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            var udfParameters = udfMethod.GetParameters().Select(p => p.ParameterType).ToList();
            udfParameters.Add(udfMethod.ReturnType);
            Type funcType = Expression.GetFuncType(udfParameters.ToArray());

            if (udfData.TargetData == null)
            {
                // The given UDF is a static function.
                return Delegate.CreateDelegate(funcType, udfMethod);
            }
            else
            {
                return Delegate.CreateDelegate(
                    funcType,
                    DeserializeTargetData(udfData.TargetData),
                    udfData.MethodName);
            }
        }

        private static TargetData SerializeTarget(object target)
        {
            // target will be null for static functions.
            if (target == null)
            {
                return null;
            }

            Type targetType = target.GetType();
            Type targetTypeData = targetType;

            var fields = new List<FieldData>();
            foreach (FieldInfo field in targetType.GetFields(
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic))
            {
                if (!field.GetCustomAttributes(typeof(NonSerializedAttribute)).Any())
                {
                    fields.Add(new FieldData()
                    {
                        TypeData = field.FieldType,
                        Name = field.Name,
                        Value = field.GetValue(target)
                    });
                }
            }

            // Even when an UDF does not have any closure, GetFields() returns some fields
            // which include Func<> of the udf specified.
            // For now, one way to distinguish is to check if any of the field's type
            // is same as the target type. If so, fields will be emptied out.
            // TODO: Follow up with the dotnet team.
            bool doesUdfHaveClosure = fields.
                Where((field) => field.TypeData.Name.Equals(targetTypeData.Name)).
                Count() == 0;

            var targetData = new TargetData()
            {
                TypeData = targetTypeData,
                Fields = doesUdfHaveClosure ? fields.ToArray() : null
            };

            return targetData;
        }

        private static object DeserializeTargetData(TargetData targetData)
        {
            Type targetType = targetData.TypeData;
            object target = FormatterServices.GetUninitializedObject(targetType);

            foreach (FieldData field in targetData.Fields ?? Enumerable.Empty<FieldData>())
            {
                targetType.GetField(
                    field.Name,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic).SetValue(target, field.Value);
            }

            return target;
        }
    }
}
