using System;
using Rxns.Collections;
using Rxns.Interfaces;

namespace Rxns.DDD.Commanding
{
    /// <summary>
    /// A question is a simple saga which runs a command then waits 
    /// for the result, conditionally publishing an rxn based on its result.
    /// </summary>
    public class Question : IRxn
    {
        public IUniqueRxn Ask { get; private set; }
        public IRxn OnSuccess { get; private set; }
        public IRxn OnFailure { get; private set; }

        public Question(IUniqueRxn ask, IRxn onSuccess = null, IRxn onFailure = null)
        {
            OnSuccess = onSuccess;
            Ask = ask;
            OnFailure = onFailure;
        }
    }

    /// <summary>
    /// This is the initial implementation of sagas in Rxns. Current we only support simple questions.
    /// 
    /// todo: 
    /// -implement more complex sagas "stories" which can be built up using a builder API
    /// which is serilisable?
    /// Ask(question).If(r => r.Id == id, new ConditionalEvent(r)).If().Always(_ => ())
    /// </summary>
    public class QuestionMediator : IRxnProcessor<Question>, IRxnProcessor<CommandResult>
    {
        private readonly IExpiringCache<string, Question> _inFlight = ExpiringCache.CreateConcurrent<string, Question>(TimeSpan.FromMinutes(10));

        public IObservable<IRxn> Process(Question @event)
        {
            return Rxn.Create(() =>
            {
                _inFlight.Set(@event.Ask.Id, @event);

                return @event.Ask;
            });
        }

        public IObservable<IRxn> Process(CommandResult @event)
        {
            return Rxn.Create(() =>
            {
                if (!_inFlight.Contains(@event.InResponseTo)) return null;

                var answer = _inFlight.Get(@event.InResponseTo);
                _inFlight.Remove(@event.InResponseTo);

                return @event.Result == CmdResult.Success ? answer.OnSuccess : answer.OnFailure;
            });
        }
    }
}
