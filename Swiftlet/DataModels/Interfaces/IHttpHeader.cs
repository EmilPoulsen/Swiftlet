﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Swiftlet.DataModels.Interfaces
{
    public interface IHttpHeader : IKeyValue
    {
        IEnumerable<KeyValuePair<string, string>> ToKvp();
    }
}
