using McOperator.Builders;
using McOperator.Entities;
using TUnit.Assertions.Extensions;

namespace McOperator.Tests;

/// <summary>
/// Tests for ConfigMapBuilder logic.
/// </summary>
public class ConfigMapBuilderTests
{
    private static MinecraftServer BuildServer(Action<MinecraftServerSpec>? configure = null)
    {
        var server = new MinecraftServer();
        server.Metadata.Name = "test-server";
        server.Metadata.NamespaceProperty = "default";
        server.Metadata.Uid = "test-uid";
        server.Spec = new MinecraftServerSpec
        {
            AcceptEula = true,
            Server = new ServerSpec { Type = ServerType.Paper, Version = "1.20.4" },
            Properties = new ServerPropertiesSpec
            {
                Difficulty = Difficulty.Normal,
                GameMode = GameMode.Survival,
                MaxPlayers = 20,
                OnlineMode = true,
                Pvp = true,
                Hardcore = false,
                LevelName = "world",
                ServerPort = 25565,
            },
        };
        configure?.Invoke(server.Spec);
        return server;
    }

    [Test]
    public async Task Build_ConfigMap_HasCorrectName()
    {
        var server = BuildServer();
        var cm = ConfigMapBuilder.Build(server);

        await Assert.That(cm.Metadata.Name).IsEqualTo("test-server-config");
    }

    [Test]
    public async Task Build_ConfigMap_HasOwnerReference()
    {
        var server = BuildServer();
        var cm = ConfigMapBuilder.Build(server);

        await Assert.That(cm.Metadata.OwnerReferences.Count).IsEqualTo(1);
        await Assert.That(cm.Metadata.OwnerReferences[0].Kind).IsEqualTo("MinecraftServer");
    }

    [Test]
    public async Task Build_ConfigMap_ContainsServerProperties()
    {
        var server = BuildServer();
        var cm = ConfigMapBuilder.Build(server);

        await Assert.That(cm.Data.ContainsKey("server.properties")).IsTrue();
        await Assert.That(cm.Data["server.properties"]).Contains("difficulty=normal");
        await Assert.That(cm.Data["server.properties"]).Contains("max-players=20");
        await Assert.That(cm.Data["server.properties"]).Contains("online-mode=true");
    }

    [Test]
    public async Task Build_ConfigMap_ContainsServerMetadata()
    {
        var server = BuildServer();
        var cm = ConfigMapBuilder.Build(server);

        await Assert.That(cm.Data.ContainsKey("server-type")).IsTrue();
        await Assert.That(cm.Data["server-type"]).IsEqualTo("Paper");
        await Assert.That(cm.Data.ContainsKey("server-version")).IsTrue();
        await Assert.That(cm.Data["server-version"]).IsEqualTo("1.20.4");
    }

    [Test]
    public async Task BuildServerPropertiesContent_IncludesDifficulty()
    {
        var props = new ServerPropertiesSpec { Difficulty = Difficulty.Hard };
        var content = ConfigMapBuilder.BuildServerPropertiesContent(props);

        await Assert.That(content).Contains("difficulty=hard");
    }

    [Test]
    public async Task BuildServerPropertiesContent_IncludesMotd_WhenSet()
    {
        var props = new ServerPropertiesSpec { Motd = "Hello World" };
        var content = ConfigMapBuilder.BuildServerPropertiesContent(props);

        await Assert.That(content).Contains("motd=Hello World");
    }

    [Test]
    public async Task BuildServerPropertiesContent_ExcludesMotd_WhenNull()
    {
        var props = new ServerPropertiesSpec { Motd = null };
        var content = ConfigMapBuilder.BuildServerPropertiesContent(props);

        await Assert.That(content).DoesNotContain("motd=");
    }

    [Test]
    public async Task BuildServerPropertiesContent_IncludesSeed_WhenSet()
    {
        var props = new ServerPropertiesSpec { LevelSeed = "12345" };
        var content = ConfigMapBuilder.BuildServerPropertiesContent(props);

        await Assert.That(content).Contains("level-seed=12345");
    }

    [Test]
    public async Task BuildServerPropertiesContent_IncludesAdditionalProperties()
    {
        var props = new ServerPropertiesSpec();
        props.AdditionalProperties["custom-key"] = "custom-value";

        var content = ConfigMapBuilder.BuildServerPropertiesContent(props);

        await Assert.That(content).Contains("custom-key=custom-value");
    }

    [Test]
    public async Task ConfigMapName_ReturnsCorrectName()
    {
        await Assert.That(ConfigMapBuilder.ConfigMapName("my-server")).IsEqualTo("my-server-config");
    }
}
