﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KIARA
{
    public interface IMessage
    {
        object Payload { get; }
    }
}