using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ProgrammePulse.Controllers;
using ProgrammePulse.Services.Integrations.GitHub;
using ProgrammePulse.Services.SkillsEvidence;

namespace ProgrammePulse.Tests.Controllers;

public class SkillsEvidenceRedirectSafetyTests
{
    [Fact]
    public void Evidence_controller_uses_local_redirect_for_actor_messages()
    {
        // The redirect helpers use no injected service, so the controller is
        // created without its constructor; the test then survives DI changes.
        var sut = Uninitialised<StaffEvidenceConnectionController>();

        var result = (LocalRedirectResult)InvokeNonPublic(
            sut,
            "Back",
            "Approved. Safe",
            GetNestedEnumValue(typeof(StaffEvidenceConnectionController), "EvidenceBackTarget", "Actors"));

        Assert.Equal("/staffops/skills/evidence/actors?message=Approved.%20Safe", result.Url);
        Assert.False(result.Permanent);
    }

    [Fact]
    public async Task Continuity_controller_routes_validation_messages_to_local_suggestions_page()
    {
        var sut = Uninitialised<StaffContinuityController>();

        var task = (Task<IActionResult>)InvokeNonPublic(
            sut,
            "RunAsync",
            new Func<Task>(() => throw new SkillAssertionValidationException("Needs review")),
            "ignored",
            GetNestedEnumValue(typeof(StaffContinuityController), "ContinuityBackTarget", "Suggestions"));

        var result = (LocalRedirectResult)await task;

        Assert.Equal("/staffops/skills/continuity/suggestions?message=Needs%20review", result.Url);
        Assert.False(result.Permanent);
    }

    private static T Uninitialised<T>() => (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static object InvokeNonPublic(object target, string methodName, params object[] args) =>
        target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target, args)!;

    private static object GetNestedEnumValue(Type declaringType, string enumName, string valueName) =>
        Enum.Parse(declaringType.GetNestedType(enumName, BindingFlags.NonPublic)!, valueName);
}
