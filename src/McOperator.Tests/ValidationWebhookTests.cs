using FluentAssertions;
using McOperator.Entities;
using McOperator.Webhooks;
using Xunit;

namespace McOperator.Tests;

/// <summary>
/// Tests for MinecraftServerValidationWebhook validation logic.
/// These cover the core business rules enforced before resources reach the reconciler.
/// </summary>
public class ValidationWebhookTests
{
    private static MinecraftServer ValidServer(Action<MinecraftServerSpec>? configure = null)
    {
        var server = new MinecraftServer
        {
            Spec = new MinecraftServerSpec
            {
                AcceptEula = true,
                Server = new ServerSpec
                {
                    Type = ServerType.Paper,
                    Version = "1.20.4",
                },
                Jvm = new JvmSpec
                {
                    InitialMemory = "512m",
                    MaxMemory = "1G",
                },
                Resources = new ResourcesSpec
                {
                    CpuRequest = "500m",
                    MemoryRequest = "1Gi",
                },
                Storage = new StorageSpec
                {
                    Enabled = true,
                    Size = "10Gi",
                    MountPath = "/data",
                },
                Service = new ServiceSpec
                {
                    Type = ServiceType.ClusterIP,
                },
                Properties = new ServerPropertiesSpec
                {
                    ServerPort = 25565,
                    MaxPlayers = 20,
                    ViewDistance = 10,
                    SimulationDistance = 10,
                    LevelName = "world",
                },
            },
        };

        configure?.Invoke(server.Spec);
        return server;
    }

    [Fact]
    public void Create_ValidServer_ReturnsSuccess()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer();

        var result = webhook.Create(server, dryRun: false);

        result.Valid.Should().BeTrue();
    }

    [Fact]
    public void Create_EulaNotAccepted_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s => s.AcceptEula = false);

        var result = webhook.Create(server, dryRun: false);

        result.Valid.Should().BeFalse();
        result.Status!.Message.Should().Contain("acceptEula");
    }

    [Fact]
    public void Create_EmptyVersion_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s => s.Server.Version = "");

        var result = webhook.Create(server, dryRun: false);

        result.Valid.Should().BeFalse();
        result.Status!.Message.Should().Contain("version");
    }

    [Fact]
    public void Create_WhitespaceVersion_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s => s.Server.Version = "   ");

        var result = webhook.Create(server, dryRun: false);

        result.Valid.Should().BeFalse();
    }

    [Fact]
    public void Create_JvmMaxLessThanInitial_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s =>
        {
            s.Jvm.InitialMemory = "2G";
            s.Jvm.MaxMemory = "1G";
        });

        var result = webhook.Create(server, dryRun: false);

        result.Valid.Should().BeFalse();
        result.Status!.Message.Should().Contain("maxMemory");
    }

    [Fact]
    public void Create_JvmEqualMemory_ReturnsSuccess()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s =>
        {
            s.Jvm.InitialMemory = "1G";
            s.Jvm.MaxMemory = "1G";
        });

        var result = webhook.Create(server, dryRun: false);

        result.Valid.Should().BeTrue();
    }

    [Fact]
    public void Create_InvalidJvmMemoryFormat_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s => s.Jvm.InitialMemory = "not-a-memory-value");

        var result = webhook.Create(server, dryRun: false);

        result.Valid.Should().BeFalse();
        result.Status!.Message.Should().Contain("initialMemory");
    }

    [Fact]
    public void Create_NodePortOnClusterIpService_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s =>
        {
            s.Service.Type = ServiceType.ClusterIP;
            s.Service.NodePort = 30000;
        });

        var result = webhook.Create(server, dryRun: false);

        result.Valid.Should().BeFalse();
        result.Status!.Message.Should().Contain("nodePort");
    }

    [Fact]
    public void Create_NodePortOnNodePortService_ReturnsSuccess()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s =>
        {
            s.Service.Type = ServiceType.NodePort;
            s.Service.NodePort = 30000;
        });

        var result = webhook.Create(server, dryRun: false);

        result.Valid.Should().BeTrue();
    }

    [Fact]
    public void Create_NodePortOutOfRange_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s =>
        {
            s.Service.Type = ServiceType.NodePort;
            s.Service.NodePort = 1234;
        });

        var result = webhook.Create(server, dryRun: false);

        result.Valid.Should().BeFalse();
        result.Status!.Message.Should().Contain("30000-32767");
    }

    [Fact]
    public void Create_InvalidStorageSize_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s => s.Storage.Size = "not-a-size");

        var result = webhook.Create(server, dryRun: false);

        result.Valid.Should().BeFalse();
        result.Status!.Message.Should().Contain("storage.size");
    }

    [Fact]
    public void Create_RelativeMountPath_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s => s.Storage.MountPath = "relative/path");

        var result = webhook.Create(server, dryRun: false);

        result.Valid.Should().BeFalse();
        result.Status!.Message.Should().Contain("mountPath");
    }

    [Fact]
    public void Create_ServerPortOutOfRange_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s => s.Properties.ServerPort = 80);

        var result = webhook.Create(server, dryRun: false);

        result.Valid.Should().BeFalse();
        result.Status!.Message.Should().Contain("serverPort");
    }

    [Fact]
    public void Create_InvalidViewDistance_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s => s.Properties.ViewDistance = 1);

        var result = webhook.Create(server, dryRun: false);

        result.Valid.Should().BeFalse();
        result.Status!.Message.Should().Contain("viewDistance");
    }

    [Fact]
    public void Update_ImmutableStorageEnabled_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var oldServer = ValidServer();
        var newServer = ValidServer(s => s.Storage.Enabled = false);

        var result = webhook.Update(newServer, oldServer, dryRun: false);

        result.Valid.Should().BeFalse();
        result.Status!.Message.Should().Contain("storage.enabled");
    }

    [Fact]
    public void Update_ImmutableStorageSize_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var oldServer = ValidServer();
        var newServer = ValidServer(s => s.Storage.Size = "20Gi");

        var result = webhook.Update(newServer, oldServer, dryRun: false);

        result.Valid.Should().BeFalse();
        result.Status!.Message.Should().Contain("storage.size");
    }

    [Fact]
    public void Update_ImmutableStorageClass_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var oldServer = ValidServer(s => s.Storage.StorageClassName = "fast");
        var newServer = ValidServer(s => s.Storage.StorageClassName = "slow");

        var result = webhook.Update(newServer, oldServer, dryRun: false);

        result.Valid.Should().BeFalse();
        result.Status!.Message.Should().Contain("storageClassName");
    }

    [Fact]
    public void Update_SameStorage_ReturnsSuccess()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var oldServer = ValidServer();
        var newServer = ValidServer(s =>
        {
            // Only change something non-storage-immutable
            s.Properties.MaxPlayers = 30;
        });

        var result = webhook.Update(newServer, oldServer, dryRun: false);

        result.Valid.Should().BeTrue();
    }

    [Fact]
    public void Create_MultipleValidationErrors_AllReported()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s =>
        {
            s.AcceptEula = false;
            s.Server.Version = "";
            s.Properties.ServerPort = 80;
        });

        var result = webhook.Create(server, dryRun: false);

        result.Valid.Should().BeFalse();
        result.Status!.Message.Should().Contain("acceptEula");
        result.Status.Message.Should().Contain("version");
        result.Status.Message.Should().Contain("serverPort");
    }
}
