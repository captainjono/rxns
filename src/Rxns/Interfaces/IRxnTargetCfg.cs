namespace Rxns.Interfaces
{
    public interface IRxnTargetCfg
    {
        IRxnRouteCfg<T> PublishTo<T>(IRxnManager<T> rxnManager);
    }
}
