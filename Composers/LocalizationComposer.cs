using Microsoft.Extensions.DependencyInjection;
using ProgrammePulse.Services.Localization;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace ProgrammePulse.Composers;

/// <summary>
/// Binds the "Localization" section: offered languages, formats, markets,
/// currencies and time zones. Cross-cutting rather than owned by one feature
/// area, so it has its own composer. See docs/localization.md.
/// </summary>
public sealed class LocalizationComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) =>
        builder.Services.Configure<LocalizationSettings>(builder.Config.GetSection(LocalizationSettings.SectionName));
}
