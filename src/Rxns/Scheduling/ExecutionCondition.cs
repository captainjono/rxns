using System;

namespace Rxns.Scheduling
{
    public class ExecutionCondition
    {
        public string Binding { get; set; }
        public object Value { get; set; }

        public Comparer Condition { get; set; }

        /// <summary>
        /// Evaluates an a set of conditions
        /// </summary>
        /// <param name="binding">The binding to evaluate</param>
        /// <param name="comparedTo">The binding to compare it against</param>
        /// <param name="condition">The condition that defines the operation</param>
        /// <returns>The result of the evaluation</returns>
        public static bool Evaluate(object binding, object comparedTo, Comparer condition)
        {
            int comparedToInt;
            int bindingInt;

            //Func<object, string> strF = str => String.IsNullOrWhiteSpace(str) ? null : str.ToString();
            
            switch (condition)
            {
                case Comparer.Is:
                    return binding.ToStringOrNull() == comparedTo.ToStringOrNull();

                case Comparer.IsNot:
                    return binding.ToStringOrNull() != comparedTo.ToStringOrNull();

                case Comparer.Gt:
                case Comparer.Lt:
                {   //if we have a string that cant parse into a number, assume the comparision is based on chars ie "2.1.3" > "2.1.4"
                    if (!Int32.TryParse(comparedTo.ToStringOrNull(), out comparedToInt) || !Int32.TryParse(binding.ToStringOrNull(), out bindingInt))
                        return condition == Comparer.Gt ? String.CompareOrdinal(binding.ToStringOrNull(), comparedTo.ToStringOrNull()) > 0 : String.CompareOrdinal(binding.ToStringOrNull(), comparedTo.ToStringOrNull()) < 0;

                    return condition == Comparer.Gt ? bindingInt > comparedToInt : bindingInt < comparedToInt;
                }

                case Comparer.Gte:
                case Comparer.Lte:
                {
                    if (!Int32.TryParse(comparedTo.ToStringOrNull(), out comparedToInt) || !Int32.TryParse(binding.ToStringOrNull(), out bindingInt))
                        return condition == Comparer.Gt ? String.CompareOrdinal(binding.ToStringOrNull(), comparedTo.ToStringOrNull()) >= 0 : String.CompareOrdinal(binding.ToStringOrNull(), comparedTo.ToStringOrNull()) <= 0;

                    return condition == Comparer.Gt ? comparedToInt >= bindingInt : comparedToInt <= bindingInt;
                }

                default:
                    return true;
            }
        }
    }

    internal static class ObjectExtensions
    {
        /// <summary>
        /// Returns null if the binding IsNullOrWhiteSpace(), otherwise, the string
        /// </summary>
        /// <param name="str">the string</param>
        /// <returns>read description</returns>
        internal static string ToStringOrNull(this object str)
        {
            return (str == null || String.IsNullOrWhiteSpace(str.ToString())) ? null : str.ToString();
        }
    }
}