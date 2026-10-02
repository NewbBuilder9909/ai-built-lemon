using Microsoft.AspNetCore.Mvc;
using ProgrammePulse.Services.ExecutiveReview;
using ProgrammePulse.Models.ExecutiveReview;
using ProgrammePulse.Services.Staff;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace ProgrammePulse.Composers;

public sealed class ExecutiveReviewComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<ExecutiveReviewOptions>().Bind(builder.Config.GetSection(ExecutiveReviewOptions.SectionName))
            .Validate(o => o.MaximumPublicationAgeHours is >= 1 and <= 168, "Publication age must be 1-168 hours.")
            .ValidateOnStart();
        builder.Services.AddScoped<IReviewAccess, ReviewAccess>();
        builder.Services.AddOptions<ExecutiveDataOptions>().Bind(builder.Config.GetSection(ExecutiveDataOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddScoped<IExecutiveDataRepository, ExecutiveDataRepository>();
        builder.Services.AddScoped<ExecutiveDataService>();
        builder.Services.AddScoped<IStaffDataParticipant, ExecutiveDataParticipant>();
        builder.Services.AddScoped<IMarketSettingsRepository, MarketSettingsRepository>();
        builder.Services.AddScoped<IExecutivePackRepository, ExecutivePackRepository>();
        builder.Services.AddScoped<ExecutiveReviewService>();
        builder.Services.AddScoped<IExecutiveDecisionRepository, ExecutiveDecisionRepository>();
        builder.Services.AddScoped<ExecutiveDecisionService>();
        builder.Services.Configure<MvcOptions>(o => o.Filters.Add(typeof(ReviewCultureFilter)));
    }
}
