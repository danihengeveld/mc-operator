using FluentAssertions;
using McOperator.Builders;
using McOperator.Entities;
using Xunit;

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

    [Fact]
    public void Build_ConfigMap_HasCorrectName()
    {
        var server = BuildServer();
        var cm = ConfigMapBuilder.Build(server);

        cm.Metadata.Name.Should().Be("test-server-config");
    }

    [Fact]
    public void Build_ConfigMap_HasOwnerReference()
    {
        var server = BuildServer();
        var cm = ConfigMapBuilder.Build(server);

        cm.Metadata.OwnerReferences.Should().HaveCount(1);
        cm.Metadata.OwnerReferences[0].Kind.Should().Be("MinecraftServer");
    }

    [Fact]
    public void Build_ConfigMap_ContainsServerProperties()
    {
        var server = BuildServer();
        var cm = ConfigMapBuilder.Build(server);

        cm.Data.Should().ContainKey("server.properties");
        cm.Data["server.properties"].Should().Contain("difficulty=normal");
        cm.Data["server.properties"].Should().Contain("max-players=20");
        cm.Data["server.properties"].Should().Contain("online-mode=true");
    }

    [Fact]
    public void Build_ConfigMap_ContainsServerMetadata()
    {
        var server = BuildServer();
        var cm = ConfigMapBuilder.Build(server);

        cm.Data.Should().ContainKey("server-type");
        cm.Data["server-type"].Should().Be("Paper");
        cm.Data.Should().ContainKey("server-version");
        cm.Data["server-version"].Should().Be("1.20.4");
    }

    [Fact]
    public void BuildServerPropertiesContent_IncludesDifficulty()
    {
        var props = new ServerPropertiesSpec { Difficulty = Difficulty.Hard };
        var content = ConfigMapBuilder.BuildServerPropertiesContent(props);

        content.Should().Contain("difficulty=hard");
    }

    [Fact]
    public void BuildServerPropertiesContent_IncludesMotd_WhenSet()
    {
        var props = new ServerPropertiesSpec { Motd = "Hello World" };
        var content = ConfigMapBuilder.BuildServerPropertiesContent(props);

        content.Should().Contain("motd=Hello World");
    }

    [Fact]
    public void BuildServerPropertiesContent_ExcludesMotd_WhenNull()
    {
        var props = new ServerPropertiesSpec { Motd = null };
        var content = ConfigMapBuilder.BuildServerPropertiesContent(props);

        content.Should().NotContain("motd=");
    }

    [Fact]
    public void BuildServerPropertiesContent_IncludesSeed_WhenSet()
    {
        var props = new ServerPropertiesSpec { LevelSeed = "12345" };
        var content = ConfigMapBuilder.BuildServerPropertiesContent(props);

        content.Should().Contain("level-seed=12345");
    }

    [Fact]
    public void BuildServerPropertiesContent_IncludesAdditionalProperties()
    {
        var props = new ServerPropertiesSpec();
        props.AdditionalProperties["custom-key"] = "custom-value";

        var content = ConfigMapBuilder.BuildServerPropertiesContent(props);

        content.Should().Contain("custom-key=custom-value");
    }

    [Fact]
    public void ConfigMapName_ReturnsCorrectName()
    {
        ConfigMapBuilder.ConfigMapName("my-server").Should().Be("my-server-config");
    }
}
