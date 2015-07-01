﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SINFONI
{
    public class SinTDStruct : SinTDType
    {
        public SinTDStruct(string name) : base(name) {
        }

        public override object AssignValuesFromObject(object other)
        {
            if (!mappings.ContainsKey(other.GetType()))
            {
                mappings.Add(other.GetType(), (MappingFunction)delegate(object other2)
                {
                    return this.MapByName(other2);
                });
            }

            if (CanBeAssignedFromType(other.GetType()))
            {
                return MapByName(other);
            }
            else /* if valid mapping is declared */
            {
                // perform mapping as declared in the mapping function
            }

            MappingFunction map = mappings[other.GetType()] as MappingFunction;
            map(other);

            return new Dictionary<string,object>();
        }

        object MapByName(object other)
        {
            var assignedMembers = new Dictionary<string, object>();

            foreach (KeyValuePair<string, SinTDType> field in members)
            {
                var SinTDValue = getFieldValueForSinTDInstance(other, field.Key, field.Value);
                assignedMembers.Add(field.Key, SinTDValue);
            }
            return  assignedMembers;
        }

        private object getFieldValueForSinTDInstance(object other, string fieldName, SinTDType SinTDType)
        {
            var assignedValue = other;
            var otherField = other.GetType().GetField(fieldName);

            if (otherField == null)
            {
                var property = other.GetType().GetProperty(fieldName);
                assignedValue = SinTDType.AssignValuesFromObject(property.GetValue(other, null));
            }
            else
            {
                assignedValue = SinTDType.AssignValuesFromObject(otherField.GetValue(other));
            }
            return assignedValue;
        }

        public override object AssignValuesToNativeType(object value, Type localType)
        {
            if (!CanBeAssignedFromType(localType))
                throw new Exceptions.TypeCastException
                    ("Cannot assign value received for SinTDStruct to native type " + localType);

            var dic = value as IDictionary;

            var localTypeInstance = Activator.CreateInstance(localType);

            foreach (string key in dic.Keys)
            {
                FieldInfo field = localType.GetField(key);
                if (field != null)
                {
                    var valueToSet = members[key].AssignValuesToNativeType(dic[key], field.FieldType);
                    field.SetValue(localTypeInstance,
                        valueToSet);
                }
                else
                {
                    PropertyInfo property = localType.GetProperty(key);
                    if (property != null)
                    {
                        property.SetValue(localTypeInstance,
                           members[key].AssignValuesToNativeType(dic[key],property.PropertyType),
                           null);
                    }
                }
            }
            return localTypeInstance;
        }

        public override bool CanBeAssignedFromType(Type type)
        {
            if (validMappings.ContainsKey(type))
                return validMappings[type];

            else
                return validMappingForTypeExists(type);
        }

        private bool validMappingForTypeExists(Type type)
        {
            if (typeof(IDictionary).IsAssignableFrom(type))
            {
                Type[] fieldTypes = type.GetGenericArguments();
                return fieldTypes[0].IsAssignableFrom(typeof(string))
                    && members.Values.All( t => t.CanBeAssignedFromType(fieldTypes[1]));
            }

            var fields = type.GetFields();
            var properties = type.GetProperties();

            foreach (KeyValuePair<string, SinTDType> member in members)
            {
                bool memberCanBeAssigned =
                    memberCanBeAssignedFromProperties(member, properties)
                    || memberCanBeAssignedFromFields(member, fields);

                if (!memberCanBeAssigned)
                {
                    validMappings[type] = false;
                    return false;
                }
            }

            validMappings[type] = true;
            return true;
        }

        private bool memberCanBeAssignedFromFields(KeyValuePair<string, SinTDType> member, FieldInfo[] fieldInfo)
        {
            int indexOfMemberInArray = Array.FindIndex(fieldInfo,
                delegate(FieldInfo element)
                {
                    bool containsElement = element.Name.Equals(member.Key);
                    return containsElement;
                });

            if (indexOfMemberInArray == -1)
                return false;

            var field = fieldInfo[indexOfMemberInArray];

            if (!member.Value.CanBeAssignedFromType(field.FieldType))
                return false;

            return true;
        }

        private bool memberCanBeAssignedFromProperties(KeyValuePair<string, SinTDType> member, PropertyInfo[] propertyInfo)
        {
            int indexOfMemberInArray = Array.FindIndex(propertyInfo,
                delegate(PropertyInfo element)
                {
                    bool containsElement = element.Name.Equals(member.Key);
                    return containsElement;
                });

            if (indexOfMemberInArray == -1)
                return false;

            var property = propertyInfo[indexOfMemberInArray];

            if (!member.Value.CanBeAssignedFromType(property.PropertyType))
                return false;

            return true;
        }

        internal Dictionary<string, SinTDType> members = new Dictionary<string, SinTDType>();
        internal Dictionary<Type, bool> validMappings = new Dictionary<Type, bool>();
        internal Dictionary<Type, Delegate> mappings = new Dictionary<Type, Delegate>();
    }
}
