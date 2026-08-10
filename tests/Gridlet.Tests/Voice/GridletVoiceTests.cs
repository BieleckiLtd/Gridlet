using Gridlet.Abstractions;
using Gridlet.Tests.AspNetCore;
using Gridlet.Tests.AspNetCore.Fakes;
using Gridlet.Voice;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gridlet.Tests.Voice;

public class GridletVoiceTests
{
    [Fact]
    public async Task Meta_omits_voice_when_the_host_did_not_add_it()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;

        var body = await client.GetStringAsync("/gridlet/api/meta");

        // A null here is what makes the UI leave the speaker button out entirely.
        Assert.Contains("\"voice\":null", body);
    }

    [Fact]
    public async Task Meta_publishes_the_configured_voice_settings()
    {
        var (app, client) = await StartWithVoiceAsync(voice =>
        {
            voice.Language = "en-GB";
            voice.Rate = 1.25;
            voice.PreferredVoice = "  Sonia  ";
        });
        await using var _ = app;

        var body = await client.GetStringAsync("/gridlet/api/meta");

        Assert.Contains("\"engine\":\"browser\"", body);
        Assert.Contains("\"language\":\"en-GB\"", body);
        Assert.Contains("\"rate\":1.25", body);
        Assert.Contains("\"preferredVoice\":\"Sonia\"", body);
        Assert.Contains("\"speakCode\":false", body);
    }

    [Fact]
    public void Network_voices_are_off_until_the_host_opts_in()
    {
        var services = new ServiceCollection();

        new GridletBuilder(services).AddVoice(voice => voice.PreferredVoice = "Sonia Online");

        // Naming a cloud voice is not itself consent to use one: the browser honours the name only
        // when the host also allowed remote synthesis.
        var info = services.BuildServiceProvider().GetRequiredService<IGridletVoiceService>().Info;
        Assert.False(info.AllowNetworkVoices);
    }

    [Fact]
    public async Task Meta_publishes_the_hosts_choice_to_allow_network_voices()
    {
        var (app, client) = await StartWithVoiceAsync(voice => voice.AllowNetworkVoices = true);
        await using var _ = app;

        var body = await client.GetStringAsync("/gridlet/api/meta");

        Assert.Contains("\"allowNetworkVoices\":true", body);
    }

    [Fact]
    public async Task Voice_settings_never_carry_a_server_address_or_credential()
    {
        var (app, client) = await StartWithVoiceAsync(_ => { });
        await using var _ = app;

        var body = await client.GetStringAsync("/gridlet/api/meta");

        // Speech happens entirely in the browser. Nothing about the host belongs in these settings.
        Assert.DoesNotContain("secret-host", body);
        Assert.Contains("\"engine\":\"browser\"", body);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(11.0)]
    public void Rate_outside_the_speech_api_range_is_rejected_at_startup(double rate)
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<GridletValidationException>(
            () => new GridletBuilder(services).AddVoice(voice => voice.Rate = rate));

        Assert.Contains("Rate", error.Message);
    }

    [Fact]
    public void Pitch_outside_the_speech_api_range_is_rejected_at_startup()
    {
        var services = new ServiceCollection();

        Assert.Throws<GridletValidationException>(
            () => new GridletBuilder(services).AddVoice(voice => voice.Pitch = 2.5));
    }

    [Fact]
    public void AddVoice_without_configuration_registers_the_browser_engine()
    {
        var services = new ServiceCollection();

        new GridletBuilder(services).AddVoice();

        var info = services.BuildServiceProvider().GetRequiredService<IGridletVoiceService>().Info;
        Assert.Equal("browser", info.Engine);
        Assert.Equal(1.0, info.Rate);
        Assert.Null(info.Language);
        Assert.False(info.SpeakCode);
        Assert.False(info.AllowNetworkVoices);
    }

    private static Task<(Microsoft.AspNetCore.Builder.WebApplication App, HttpClient Client)>
        StartWithVoiceAsync(Action<GridletVoiceOptions> configure) =>
        GridletTestHost.StartAsync(
            options =>
            {
                options.AddConnection("Main", "Server=secret-host;", FakeGridletProvider.Name);
                options.Security.AllowAnonymous = true;
            },
            services => new GridletBuilder(services).AddVoice(configure));
}
