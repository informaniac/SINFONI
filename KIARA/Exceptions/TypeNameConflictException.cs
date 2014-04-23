﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KIARA.Exceptions
{
    public class TypeNameConflictException : Exception
    {
        public TypeNameConflictException(string name)
            : base("A type with name " + name + " is already registered")
        { }
    }
}