using System;
using System.Reactive.Linq;
using Rxns.DDD.BoundedContext;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Logging;
using RxnsDemo.Micro.App.AggRoots;
using RxnsDemo.Micro.App.Cmds;
using RxnsDemo.Micro.App.Models;
using RxnsDemo.Micro.App.Qrys;

namespace RxnsDemo.Micro.App.Api
{
    public class SurveyAnswersDomainService : IDomainQueryHandler<LookupProgressInSurveyQry, SurveyProgressModel>,
                                               IDomainCommandHandler<RecordAnswerForSurveyCmd, Guid>,
                                               IDomainCommandHandler<BeginSurveyCmd, Guid>,
                                               IDomainCommandHandler<FinishSurveyCmd, Guid>
    {
        private readonly ITenantModelRepository<SurveyAnswers> _answersRepo;
        private readonly ISurveyProgressView _progress;

        public SurveyAnswersDomainService(ITenantModelRepository<SurveyAnswers> answersRepo, ISurveyProgressView progress)
        {
            _answersRepo = answersRepo;
            _progress = progress;
        }

        public IObservable<SurveyProgressModel> Handle(LookupProgressInSurveyQry query)
        {
            $"Handle(LookupAssessments) called for tenant {query.Tenant}".LogDebug();

            return _progress.ForUser(query.UserId, query.AttemptId);
        }

        public IObservable<DomainCommandResult<Guid>> Handle(RecordAnswerForSurveyCmd answer)
        {
            return _answersRepo.GetById(answer.UserId, answer.SurveyId).Select(attempt =>
            {
                $"Recorded answer for SurveyUser {answer.UserId}".LogDebug();

                attempt.Record(new AnswerModel()
                {
                    Answer = answer.Answer,
                    QuestionId = answer.QuestionId
                });

                return _answersRepo.Save(answer.UserId, attempt).AsSideEffectsOfResult(answer.Id, Guid.NewGuid());
            });
        }

        public IObservable<DomainCommandResult<Guid>> Handle(BeginSurveyCmd begun)
        {
            return _answersRepo.GetById(begun.UserId, begun.AttemptId).Select(attempt =>
            {
                $"Starting test attempt for SurveyUser {begun.UserId}".LogDebug();

                attempt.Start(begun.UserId, begun.AttemptId);

                return _answersRepo.Save(begun.UserId, attempt).AsSideEffectsOfResult(begun.Id, Guid.NewGuid());
            });
        }

        public IObservable<DomainCommandResult<Guid>> Handle(FinishSurveyCmd finished)
        {
            return _answersRepo.GetById(finished.UserId, finished.SurveyId).Select(attempt =>
            {
                $"SurveyUser {finished.UserId} finished attempt {finished.SurveyId} ".LogDebug();

                attempt.End();

                return _answersRepo.Save(finished.UserId, attempt).AsSideEffectsOfResult(finished.Id, Guid.NewGuid());
            });
        }
    }
}
