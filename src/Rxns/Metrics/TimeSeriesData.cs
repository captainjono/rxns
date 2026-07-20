using System;
using System.Dynamic;
using Rxns.Interfaces;

namespace Rxns.Metrics
{
    public class TimeSeriesData : IRxn
    {
        public string Name { get; set; }
        public DateTime TimeStamp { get; set; }
        public dynamic Value { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is TimeSeriesData)
                return Equals(obj as TimeSeriesData);

            return false;
        }

        protected bool Equals(TimeSeriesData other)
        {
            return string.Equals(Name, other.Name);
        }

        public override int GetHashCode()
        {
            return (Name != null ? Name.GetHashCode() : 0);
        }
    }
}
