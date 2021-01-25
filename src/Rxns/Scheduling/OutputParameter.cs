namespace Rxns.Scheduling
{
    public class OutputParameter
    {
        public string Name { get; set; }
        public object Value { get; set; }
        public string Parameter { get; set; }
        public string DataType { get; set; }

        public override string ToString()
        {
            return Value.ToString();
        }
    }
}
