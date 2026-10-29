using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Globeweigh.Model;

namespace Globeweigh.Shared.Helpers
{
    public static class EnumHelper
    {
        public static Dictionary<int, string> GetEnumList(Type type)
        {
            var descs = new Dictionary<int,string>();
            var names = Enum.GetNames(type);
            foreach (var name in names)
            {
                var field = type.GetField(name);
                var fds = field.GetCustomAttributes(typeof(DescriptionAttribute), true);
                var value = (int)field.GetValue(field);
                foreach (DescriptionAttribute fd in fds)
                {
                    descs.Add(value, fd.Description);
                }
            }
            return descs;
        }


        public static List<ReferenceData> GetReferenceDataListFromEnum(Type type)
        {
            var descriptionList = GetEnumList(type);
            return descriptionList.Select(item => new ReferenceData {DisplayName = item.Value, id = item.Key}).ToList();
        }
    }
}
