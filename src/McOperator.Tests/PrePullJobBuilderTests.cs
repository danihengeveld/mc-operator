using McOperator.Builders;
using McOperator.Entities;

namespace McOperator.Tests;

/// <summary>
/// Tests for PrePullJobBuilder resource building logic.
/// </summary>
public class PrePullJobBuilderTests
{
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

    [Test]
    public async Task Build_Container_OverridesEntrypointToExitImmediately()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        var container = job.Spec.Template.Spec.Containers[0];
        await Assert.That(container.Command).IsNotNull();
        await Assert.That(container.Command).Contains("exit 0");
    }

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
    public async Task Build_Container_HasMinimalResourceRequests()
    {
        var server = BuildServer();
        var job = PrePullJobBuilder.Build(server, "itzg/minecraft-server:latest", null);

        var container = job.Spec.Template.Spec.Containers[0];
        await Assert.That(container.Resources).IsNotNull();
        await Assert.That(container.Resources.Requests.ContainsKey("cpu")).IsTrue();
        await Assert.That(container.Resources.Requests.ContainsKey("memory")).IsTrue();
    }
}
