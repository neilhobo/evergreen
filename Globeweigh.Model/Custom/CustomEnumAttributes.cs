using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Globeweigh.Model.Custom
{
    class ExcludeAttribute : Attribute
    {
        public bool Exclude { get; set; }
        public ExcludeAttribute(bool exclude)
        {
            this.Exclude = exclude;
        }
    }
}
