using System;
using System.Reactive.Linq;
using Rxns;
using Rxns.Interfaces;
using RxnsDemo.Micro.App.Events;
using RxnsDemo.Micro.App.Models;

namespace RxnsDemo.Micro.App.Api
{
    public interface ISurveyProgressView : IRxnProcessor<UserAnsweredQuestionEvent>, 
                                            IRxnProcessor<UserSurveyStartedEvent>, 
                                            IRxnProcessor<UserSurveyEndedEvent>
    {
        IObservable<SurveyProgressModel> ForUser(string userId, string surveyId);
    }

    public class SurveyProgressView : ISurveyProgressView
    {
        /// <summary>
        /// KeyvalueStore should possibly be simply a Idictionary
        // might want to swap this out to standarize API. 
        // this use a rxndictionary might be more suitable, less overhead and the operations map well.
        // but i dont have a filesystem reactiondictionary impl? what would that look like? a snapshot/cache of the model?
        /// </summary>
        private readonly IKeyValueStore<string, SurveyProgressModel> _cache;

        
        /// <param name="cache"></param>
        public SurveyProgressView(IKeyValueStore<string, SurveyProgressModel> cache)
        {
            _cache = cache;
        }

        public IObservable<IRxn> Process(UserAnsweredQuestionEvent @event)
        {
            return GetOrCreate(@event.UserId, @event.SurveyId).SelectMany(r =>
            {
                r.Answered++;
                Save(@event.UserId, @event.SurveyId, r);

                return Rxn.Empty<IRxn>();
            });
        }

        private IObservable<SurveyProgressModel> GetOrCreate(string userId, string attemptId)
        {
            return _cache.Get($"{userId}_{attemptId}");
        }

        private IObservable<bool> Save(string userId, string attemptId, SurveyProgressModel surveyProgressModel)
        {
            return _cache.AddOrUpdate($"{userId}_{attemptId}", surveyProgressModel);
        }

        public IObservable<IRxn> Process(UserSurveyStartedEvent @event)
        {
            return GetOrCreate(@event.UserId, @event.SurveyId).SelectMany(r =>
            {
                r.StartTime = @event.Timestamp;
                Save(@event.UserId, @event.SurveyId, r);

                return Rxn.Empty<IRxn>();
            });
        }

        public IObservable<IRxn> Process(UserSurveyEndedEvent @event)
        {
            return GetOrCreate(@event.SurveyId, @event.SurveyId).SelectMany(r =>
            {
                r.EndTime = @event.Timestamp;
                Save(@event.SurveyId, @event.SurveyId, r);

                return Rxn.Empty<IRxn>();
            });
        }

        public IObservable<SurveyProgressModel> ForUser(string userId, string surveyId)
        {
            return GetOrCreate(userId, surveyId);
        }
    }
}
