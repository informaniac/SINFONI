﻿// This file is part of SINFONI.
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 3.0 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library.  If not, see <http://www.gnu.org/licenses/>.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SINFONI
{
    public class SinTDStruct : SinTDType
    {
        public SinTDStruct(string name) : base(name) {
            typeBuilder = structBuilder.CreateTypeBuilder(name);
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
            if (other == null)
                return null;

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

        public override Type InstanceType {
            get
            {
                if(nativeType == null)
                {
                    nativeType = typeBuilder.CreateType();
                }

                return nativeType;
            }
        }

        public override bool CanBeAssignedFromType(Type type)
        {
            if (validMappings.ContainsKey(type))
                return validMappings[type];

            else
                return validMappingForTypeExists(type);
        }

        /// <summary>
        /// Adds a new member field to the struct. It will be assigned the provided name, and use the type
        /// that was previously created for the added SinTDType. Afterwards, the native type of this SinTD
        /// struct will be rebuilt
        /// </summary>
        /// <param name="name">Name of the new member field</param>
        /// <param name="type">SinTDType of the new member</param>
        internal void AddMember(string name, SinTDType type)
        {
            this.members.Add(name, type);
            typeBuilder.DefineField(name, type.InstanceType, FieldAttributes.Public);
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

        internal Type nativeType;
        private StructBuilder structBuilder = new StructBuilder();
        internal Dictionary<Type, bool> validMappings = new Dictionary<Type, bool>();
        internal Dictionary<Type, Delegate> mappings = new Dictionary<Type, Delegate>();

        internal Dictionary<string, SinTDType> members = new Dictionary<string, SinTDType>();
        private TypeBuilder typeBuilder;
    }
}
