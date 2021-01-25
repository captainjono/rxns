using System;

namespace Rxns.Interfaces
{
    public interface IRxnTargetCfg
    {
        IRxnRouteCfg<T> PublishTo<T>(Action<T> rxnManager);
    }
}
