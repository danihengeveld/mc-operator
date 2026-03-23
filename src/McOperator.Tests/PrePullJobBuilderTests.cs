using McOperator.Builders;
using McOperator.Entities;

namespace McOperator.Tests;

/// <summary>
/// Tests for PrePullJobBuilder resource building logic.
/// </summary>
public class PrePullJobBuilderTests
{
    // Default server: no custom image, storage enabled → jar-download mode.
    private static MinecraftServer BuildServer(string name = "test-server", string ns = "default")
    {
        var server = new MinecraftServer();
        server.Metadata.Name = name;
        server.Metadata.NamespaceProperty = ns;
        server.Metadata.Uid = "test-uid-5678";
        server.Spec = new MinecraftServerSpec
        {
            AcceptEula = true,
            Server = new ServerSpec { Type = ServerType.Paper, Version = "1.20.4" },
        };
        return server;
    }

    // Server with a custom image override → OCI-only mode.
    private static MinecraftServer BuildServerWithCustomImage(string name = "test-server", string ns = "default")
    {
        var server = BuildServer(name, ns);
        server.Spec.Image = "custom/image:v1";
        return server;
    }

    // Server with storage disabled → OCI-only mode even for the default itzg image.
    private static MinecraftServer BuildServerWithStorageDisabled(string name = "test-server", string ns = "default")
    {
        var server = BuildServer(name, ns);
        server.Spec.Storage = new StorageSpec { Enabled = false };
        return server;
    }

    [Test]
    public async Task JobName_FollowsNamingConvention()
    {
        var server = BuildServer("my-server");

        await Assert.That(PrePullJobBuilder.JobName(server)).IsEqualTo("my-server-prepull");
    }

    [Test]
    public async Task Build_Job_HasCorrectName()
    {
        var server = BuildServer("my-server");
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:2025.1.0", null);

        await Assert.That(job.Metadata.Name).IsEqualTo("my-server-prepull");
    }

    [Test]
    public async Task Build_Job_HasCorrectNamespace()
    {
        var server = BuildServer(ns: "production");
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        await Assert.That(job.Metadata.NamespaceProperty).IsEqualTo("production");
    }

    [Test]
    public async Task Build_Job_HasOwnerReference()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        await Assert.That(job.Metadata.OwnerReferences).IsNotNull();
        await Assert.That(job.Metadata.OwnerReferences.Count).IsEqualTo(1);
        await Assert.That(job.Metadata.OwnerReferences[0].Kind).IsEqualTo("MinecraftServer");
        await Assert.That(job.Metadata.OwnerReferences[0].Name).IsEqualTo("test-server");
        await Assert.That(job.Metadata.OwnerReferences[0].Uid).IsEqualTo("test-uid-5678");
        await Assert.That(job.Metadata.OwnerReferences[0].Controller).IsTrue();
    }

    [Test]
    public async Task Build_Container_UsesDesiredImage()
    {
        var server = BuildServer();
        var image = "my-registry/minecraft:paper-1.21.0";
        var job = PrePullJobBuilder.Build(server, image, null);

        var container = job.Spec.Template.Spec.Containers[0];
        await Assert.That(container.Image).IsEqualTo(image);
    }

    [Test]
    public async Task Build_Container_HasIfNotPresentPullPolicy()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        var container = job.Spec.Template.Spec.Containers[0];
        await Assert.That(container.ImagePullPolicy).IsEqualTo("IfNotPresent");
    }

    // -----------------------------------------------------------------------
    // OCI-only mode (custom image)
    // -----------------------------------------------------------------------

    [Test]
    public async Task Build_Container_WithCustomImage_ExitsImmediately()
    {
        var server = BuildServerWithCustomImage();
        var job = PrePullJobBuilder.Build(server, "custom/image:v1", null);

        var container = job.Spec.Template.Spec.Containers[0];
        await Assert.That(container.Command).IsNotNull();
        // The command must include "exit 0" so no server logic ever runs.
        await Assert.That(string.Join(" ", container.Command)).Contains("exit 0");
    }

    [Test]
    public async Task Build_Container_WithStorageDisabled_ExitsImmediately()
    {
        var server = BuildServerWithStorageDisabled();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        var container = job.Spec.Template.Spec.Containers[0];
        await Assert.That(container.Command).IsNotNull();
        await Assert.That(string.Join(" ", container.Command)).Contains("exit 0");
    }

    [Test]
    public async Task Build_Container_WithCustomImage_HasNoVolumeMount()
    {
        var server = BuildServerWithCustomImage();
        var job = PrePullJobBuilder.Build(server, "custom/image:v1", null);

        var container = job.Spec.Template.Spec.Containers[0];
        var hasMount = container.VolumeMounts?.Any() == true;
        await Assert.That(hasMount).IsFalse();
    }

    [Test]
    public async Task Build_PodSpec_WithCustomImage_HasNoVolumes()
    {
        var server = BuildServerWithCustomImage();
        var job = PrePullJobBuilder.Build(server, "custom/image:v1", null);

        var hasVolumes = job.Spec.Template.Spec.Volumes?.Any() == true;
        await Assert.That(hasVolumes).IsFalse();
    }

    // -----------------------------------------------------------------------
    // Jar-download mode (default itzg image + storage enabled)
    // -----------------------------------------------------------------------

    [Test]
    public async Task Build_Container_WithDefaultImage_RunsStartupWithFakeJava()
    {
        // Default server has no custom image and storage enabled → jar-download mode.
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        var container = job.Spec.Template.Spec.Containers[0];
        await Assert.That(container.Command).IsNotNull();

        var fullCommand = string.Join(" ", container.Command);
        // Verifies the fake java stub is set up and the itzg start script is invoked.
        await Assert.That(fullCommand).Contains("JAVA_CMD");
        await Assert.That(fullCommand).Contains("/start");
    }

    [Test]
    public async Task Build_Container_WithDefaultImage_HasDataVolumeMount()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        var container = job.Spec.Template.Spec.Containers[0];
        var dataMount = container.VolumeMounts?.FirstOrDefault(m => m.Name == "data");
        await Assert.That(dataMount).IsNotNull();
        await Assert.That(dataMount!.MountPath).IsEqualTo(server.Spec.Storage.MountPath);
    }

    [Test]
    public async Task Build_PodSpec_WithDefaultImage_HasDataVolume()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        var dataVolume = job.Spec.Template.Spec.Volumes?.FirstOrDefault(v => v.Name == "data");
        await Assert.That(dataVolume).IsNotNull();
        // The claim name follows the StatefulSet convention: data-<server>-0
        await Assert.That(dataVolume!.PersistentVolumeClaim!.ClaimName).IsEqualTo("data-test-server-0");
    }

    [Test]
    public async Task Build_Container_WithDefaultImage_HasEulaEnvVar()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        var container = job.Spec.Template.Spec.Containers[0];
        var eulaEnv = container.Env?.FirstOrDefault(e => e.Name == "EULA");
        await Assert.That(eulaEnv).IsNotNull();
        await Assert.That(eulaEnv!.Value).IsEqualTo("TRUE");
    }

    [Test]
    public async Task Build_Container_WithDefaultImage_HasTypeAndVersionEnvVars()
    {
        var server = BuildServer();
        server.Spec.Server.Type = ServerType.Paper;
        server.Spec.Server.Version = "1.20.4";
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        var container = job.Spec.Template.Spec.Containers[0];
        var typeEnv = container.Env?.FirstOrDefault(e => e.Name == "TYPE");
        var versionEnv = container.Env?.FirstOrDefault(e => e.Name == "VERSION");

        await Assert.That(typeEnv).IsNotNull();
        await Assert.That(typeEnv!.Value).IsEqualTo("PAPER");
        await Assert.That(versionEnv).IsNotNull();
        await Assert.That(versionEnv!.Value).IsEqualTo("1.20.4");
    }

    [Test]
    public async Task Build_Container_WithDefaultImage_HasSkipServerPropertiesEnvVar()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        var container = job.Spec.Template.Spec.Containers[0];
        var skipEnv = container.Env?.FirstOrDefault(e => e.Name == "SKIP_SERVER_PROPERTIES");
        await Assert.That(skipEnv).IsNotNull();
        await Assert.That(skipEnv!.Value).IsEqualTo("true");
    }

    [Test]
    public async Task Build_Container_WithDefaultImage_WorkingDirIsStorageMountPath()
    {
        var server = BuildServer();
        server.Spec.Storage.MountPath = "/data";
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        var container = job.Spec.Template.Spec.Containers[0];
        await Assert.That(container.WorkingDir).IsEqualTo("/data");
    }

    // -----------------------------------------------------------------------
    // Common job spec
    // -----------------------------------------------------------------------

    [Test]
    public async Task Build_Job_HasBackoffLimitZero()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        await Assert.That(job.Spec.BackoffLimit).IsEqualTo(0);
    }

    [Test]
    public async Task Build_Job_HasActiveDeadlineSeconds()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        await Assert.That(job.Spec.ActiveDeadlineSeconds).IsNotNull();
        await Assert.That(job.Spec.ActiveDeadlineSeconds!.Value).IsGreaterThan(0);
    }

    [Test]
    public async Task Build_Job_HasTtlSecondsAfterFinished()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        await Assert.That(job.Spec.TtlSecondsAfterFinished).IsNotNull();
        await Assert.That(job.Spec.TtlSecondsAfterFinished!.Value).IsGreaterThan(0);
    }

    [Test]
    public async Task Build_PodSpec_HasNeverRestartPolicy()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        await Assert.That(job.Spec.Template.Spec.RestartPolicy).IsEqualTo("Never");
    }

    [Test]
    public async Task Build_PodSpec_HasNodeName_WhenNodeNameProvided()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", "worker-node-1");

        await Assert.That(job.Spec.Template.Spec.NodeName).IsEqualTo("worker-node-1");
    }

    [Test]
    public async Task Build_PodSpec_HasNoNodeName_WhenNodeNameIsNull()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        await Assert.That(job.Spec.Template.Spec.NodeName).IsNull();
    }

    [Test]
    public async Task Build_Job_HasManagedByLabel()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        await Assert.That(job.Metadata.Labels.ContainsKey("app.kubernetes.io/managed-by")).IsTrue();
        await Assert.That(job.Metadata.Labels["app.kubernetes.io/managed-by"]).IsEqualTo("mc-operator");
    }

    [Test]
    public async Task Build_Job_HasPrePullLabel()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        await Assert.That(job.Metadata.Labels.ContainsKey("mc-operator.dhv.sh/pre-pull")).IsTrue();
        await Assert.That(job.Metadata.Labels["mc-operator.dhv.sh/pre-pull"]).IsEqualTo("true");
    }

    [Test]
    public async Task Build_Job_HasServerNameLabel()
    {
        var server = BuildServer("my-server");
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        await Assert.That(job.Metadata.Labels.ContainsKey("mc-operator.dhv.sh/server-name")).IsTrue();
        await Assert.That(job.Metadata.Labels["mc-operator.dhv.sh/server-name"]).IsEqualTo("my-server");
    }

    [Test]
    public async Task Build_Job_HasCorrectApiVersionAndKind()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        await Assert.That(job.ApiVersion).IsEqualTo("batch/v1");
        await Assert.That(job.Kind).IsEqualTo("Job");
    }

    [Test]
    public async Task Build_Container_HasResourceRequests()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        var container = job.Spec.Template.Spec.Containers[0];
        await Assert.That(container.Resources).IsNotNull();
        await Assert.That(container.Resources.Requests.ContainsKey("cpu")).IsTrue();
        await Assert.That(container.Resources.Requests.ContainsKey("memory")).IsTrue();
    }
}
