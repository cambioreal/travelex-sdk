using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace CambioReal.Travelex.Tests;

public sealed class StartupValidationTests
{
    [Fact]
    public void InvalidOptionsFailThroughTheStandardStartupValidator()
    {
        var services = new ServiceCollection();
        services.AddTravelexClient(_ => { });

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IStartupValidator>();

        Should.Throw<OptionsValidationException>(validator.Validate);
    }

    [Fact]
    public void ValidOptionsPassThroughTheStandardStartupValidator()
    {
        var services = new ServiceCollection();
        services.AddTravelexClient(options => { options.Username = "user"; options.Password = "password"; options.BranchNumber = "branch"; options.AccountNumber = "account"; options.CertificatePem = "certificate"; options.PrivateKeyPem = "private-key"; });

        using var provider = services.BuildServiceProvider();

        Should.NotThrow(provider.GetRequiredService<IStartupValidator>().Validate);
    }
}
