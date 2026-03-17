using McOperator.Entities;
using McOperator.Webhooks;
using TUnit.Assertions.Extensions;

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

    [Test]
    public async Task Create_ValidServer_ReturnsSuccess()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer();

        var result = webhook.Create(server, dryRun: false);

        await Assert.That(result.Valid).IsTrue();
    }

    [Test]
    public async Task Create_EulaNotAccepted_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s => s.AcceptEula = false);

        var result = webhook.Create(server, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("acceptEula");
    }

    [Test]
    public async Task Create_EmptyVersion_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s => s.Server.Version = "");

        var result = webhook.Create(server, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("version");
    }

    [Test]
    public async Task Create_WhitespaceVersion_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s => s.Server.Version = "   ");

        var result = webhook.Create(server, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
    }

    [Test]
    public async Task Create_JvmMaxLessThanInitial_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s =>
        {
            s.Jvm.InitialMemory = "2G";
            s.Jvm.MaxMemory = "1G";
        });

        var result = webhook.Create(server, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("maxMemory");
    }

    [Test]
    public async Task Create_JvmEqualMemory_ReturnsSuccess()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s =>
        {
            s.Jvm.InitialMemory = "1G";
            s.Jvm.MaxMemory = "1G";
        });

        var result = webhook.Create(server, dryRun: false);

        await Assert.That(result.Valid).IsTrue();
    }

    [Test]
    public async Task Create_InvalidJvmMemoryFormat_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s => s.Jvm.InitialMemory = "not-a-memory-value");

        var result = webhook.Create(server, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("initialMemory");
    }

    [Test]
    public async Task Create_NodePortOnClusterIpService_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s =>
        {
            s.Service.Type = ServiceType.ClusterIP;
            s.Service.NodePort = 30000;
        });

        var result = webhook.Create(server, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("nodePort");
    }

    [Test]
    public async Task Create_NodePortOnNodePortService_ReturnsSuccess()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s =>
        {
            s.Service.Type = ServiceType.NodePort;
            s.Service.NodePort = 30000;
        });

        var result = webhook.Create(server, dryRun: false);

        await Assert.That(result.Valid).IsTrue();
    }

    [Test]
    public async Task Create_NodePortOutOfRange_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s =>
        {
            s.Service.Type = ServiceType.NodePort;
            s.Service.NodePort = 1234;
        });

        var result = webhook.Create(server, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("30000-32767");
    }

    [Test]
    public async Task Create_InvalidStorageSize_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s => s.Storage.Size = "not-a-size");

        var result = webhook.Create(server, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("storage.size");
    }

    [Test]
    public async Task Create_RelativeMountPath_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s => s.Storage.MountPath = "relative/path");

        var result = webhook.Create(server, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("mountPath");
    }

    [Test]
    public async Task Create_ServerPortOutOfRange_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s => s.Properties.ServerPort = 80);

        var result = webhook.Create(server, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("serverPort");
    }

    [Test]
    public async Task Create_InvalidViewDistance_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s => s.Properties.ViewDistance = 1);

        var result = webhook.Create(server, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("viewDistance");
    }

    [Test]
    public async Task Update_ImmutableStorageEnabled_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var oldServer = ValidServer();
        var newServer = ValidServer(s => s.Storage.Enabled = false);

        var result = webhook.Update(newServer, oldServer, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("storage.enabled");
    }

    [Test]
    public async Task Update_ImmutableStorageSize_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var oldServer = ValidServer();
        var newServer = ValidServer(s => s.Storage.Size = "20Gi");

        var result = webhook.Update(newServer, oldServer, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("storage.size");
    }

    [Test]
    public async Task Update_ImmutableStorageClass_ReturnsFail()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var oldServer = ValidServer(s => s.Storage.StorageClassName = "fast");
        var newServer = ValidServer(s => s.Storage.StorageClassName = "slow");

        var result = webhook.Update(newServer, oldServer, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("storageClassName");
    }

    [Test]
    public async Task Update_SameStorage_ReturnsSuccess()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var oldServer = ValidServer();
        var newServer = ValidServer(s =>
        {
            // Only change something non-storage-immutable
            s.Properties.MaxPlayers = 30;
        });

        var result = webhook.Update(newServer, oldServer, dryRun: false);

        await Assert.That(result.Valid).IsTrue();
    }

    [Test]
    public async Task Create_MultipleValidationErrors_AllReported()
    {
        var webhook = new MinecraftServerValidationWebhook();
        var server = ValidServer(s =>
        {
            s.AcceptEula = false;
            s.Server.Version = "";
            s.Properties.ServerPort = 80;
        });

        var result = webhook.Create(server, dryRun: false);

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Status!.Message).Contains("acceptEula");
        await Assert.That(result.Status.Message).Contains("version");
        await Assert.That(result.Status.Message).Contains("serverPort");
    }
}
