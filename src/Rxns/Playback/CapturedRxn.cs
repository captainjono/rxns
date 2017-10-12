using System;
using Rxns.Interfaces;

namespace Rxns.Playback
{
    public class CapturedRxn : ICapturedRxn
    {
        public TimeSpan Offset { get; private set; }
        public IRxn Recorded { get; private set; }

        public CapturedRxn(TimeSpan offSet, IRxn rxn)
        {
            Offset = offSet;
            Recorded = rxn;
        }

        public CapturedRxn() { }
    }
}