using McOperator.Builders;
using McOperator.Entities;

namespace McOperator.Tests;

/// <summary>
/// Tests for VelocityProxyBuilder resource building logic.
/// </summary>
public class VelocityProxyBuilderTests
{
    private static MinecraftServerCluster BuildCluster(Action<MinecraftServerClusterSpec>? configure = null)
    {
        var cluster = new MinecraftServerCluster
        {
            Metadata = { Name = "test-cluster", NamespaceProperty = "default", Uid = "cluster-uid-1234" },
            Spec = new MinecraftServerClusterSpec
            {
                Template = new MinecraftServerTemplate
                {
                    AcceptEula = true,
                    Server = new ServerSpec { Type = ServerType.Paper, Version = "1.20.4" },
                    Properties = new ServerPropertiesSpec { ServerPort = 25565 },
                },
                Scaling = new ScalingSpec { Mode = ScalingMode.Static, Replicas = 3 },
                Proxy = new VelocityProxySpec
                {
                    ProxyPort = 25577,
                    MaxPlayers = 100,
                    OnlineMode = true,
                    PlayerForwardingMode = PlayerForwardingMode.Modern,
                    Service = new ServiceSpec { Type = ServiceType.ClusterIP },
                    Resources = new ResourcesSpec { CpuRequest = "500m", MemoryRequest = "512Mi", },
                },
            }
        };
        configure?.Invoke(cluster.Spec);
        return cluster;
    }

    private static List<ServerAddress> BuildServerAddresses(int count = 3)
    {
        return Enumerable.Range(0, count).Select(i => new ServerAddress
        {
            Name = $"server-{i}", Address = $"test-cluster-{i}", Port = 25565,
        }).ToList();
    }

    // ========== Deployment Tests ==========

    [Test]
    public async Task BuildDeployment_HasCorrectName()
    {
        var cluster = BuildCluster();
        var deployment = VelocityProxyBuilder.BuildDeployment(cluster);

        await Assert.That(deployment.Metadata.Name).IsEqualTo("test-cluster-proxy");
        await Assert.That(deployment.Metadata.NamespaceProperty).IsEqualTo("default");
    }

    [Test]
    public async Task BuildDeployment_HasOwnerReference()
    {
        var cluster = BuildCluster();
        var deployment = VelocityProxyBuilder.BuildDeployment(cluster);

        await Assert.That(deployment.Metadata.OwnerReferences.Count).IsEqualTo(1);
        await Assert.That(deployment.Metadata.OwnerReferences[0].Kind).IsEqualTo("MinecraftServerCluster");
        await Assert.That(deployment.Metadata.OwnerReferences[0].Name).IsEqualTo("test-cluster");
        await Assert.That(deployment.Metadata.OwnerReferences[0].Uid).IsEqualTo("cluster-uid-1234");
        await Assert.That(deployment.Metadata.OwnerReferences[0].Controller).IsTrue();
    }

    [Test]
    public async Task BuildDeployment_HasOneReplica()
    {
        var cluster = BuildCluster();
        var deployment = VelocityProxyBuilder.BuildDeployment(cluster);

        await Assert.That(deployment.Spec.Replicas).IsEqualTo(1);
    }

    [Test]
    public async Task BuildDeployment_Container_HasCorrectImage_Default()
    {
        var cluster = BuildCluster();
        var deployment = VelocityProxyBuilder.BuildDeployment(cluster);

        var container = deployment.Spec.Template.Spec.Containers[0];
        await Assert.That(container.Image).IsEqualTo("itzg/mc-proxy:latest");
    }

    [Test]
    public async Task BuildDeployment_Container_UsesCustomImage_WhenSpecified()
    {
        var cluster = BuildCluster(s => s.Proxy.Image = "my-custom-proxy:1.0");
        var deployment = VelocityProxyBuilder.BuildDeployment(cluster);

        var container = deployment.Spec.Template.Spec.Containers[0];
        await Assert.That(container.Image).IsEqualTo("my-custom-proxy:1.0");
    }

    [Test]
    public async Task BuildDeployment_Container_HasTypeEnvVar()
    {
        var cluster = BuildCluster();
        var deployment = VelocityProxyBuilder.BuildDeployment(cluster);

        var container = deployment.Spec.Template.Spec.Containers[0];
        var typeEnv = container.Env.FirstOrDefault(e => e.Name == "TYPE");
        await Assert.That(typeEnv).IsNotNull();
        await Assert.That(typeEnv!.Value).IsEqualTo("VELOCITY");
    }

    [Test]
    public async Task BuildDeployment_Container_HasCorrectPort()
    {
        var cluster = BuildCluster(s => s.Proxy.ProxyPort = 25577);
        var deployment = VelocityProxyBuilder.BuildDeployment(cluster);

        var container = deployment.Spec.Template.Spec.Containers[0];
        await Assert.That(container.Ports[0].ContainerPort).IsEqualTo(25577);
    }

    [Test]
    public async Task BuildDeployment_Container_HasConfigVolumeMount()
    {
        var cluster = BuildCluster();
        var deployment = VelocityProxyBuilder.BuildDeployment(cluster);

        var container = deployment.Spec.Template.Spec.Containers[0];
        var configMount = container.VolumeMounts.FirstOrDefault(m => m.MountPath == "/server/velocity.toml");
        await Assert.That(configMount).IsNotNull();
        await Assert.That(configMount!.SubPath).IsEqualTo("velocity.toml");
    }

    [Test]
    public async Task BuildDeployment_Container_HasForwardingSecretVolumeMount()
    {
        var cluster = BuildCluster();
        var deployment = VelocityProxyBuilder.BuildDeployment(cluster);

        var container = deployment.Spec.Template.Spec.Containers[0];
        var secretMount = container.VolumeMounts.FirstOrDefault(m => m.MountPath == "/server/forwarding.secret");
        await Assert.That(secretMount).IsNotNull();
        await Assert.That(secretMount!.SubPath).IsEqualTo("forwarding.secret");
    }

    [Test]
    public async Task BuildDeployment_Container_HasReadinessAndLivenessProbes()
    {
        var cluster = BuildCluster();
        var deployment = VelocityProxyBuilder.BuildDeployment(cluster);

        var container = deployment.Spec.Template.Spec.Containers[0];
        await Assert.That(container.ReadinessProbe).IsNotNull();
        await Assert.That(container.LivenessProbe).IsNotNull();
    }

    [Test]
    public async Task BuildDeployment_Container_HasResourceRequests()
    {
        var cluster = BuildCluster(s =>
        {
            s.Proxy.Resources.CpuRequest = "500m";
            s.Proxy.Resources.MemoryRequest = "512Mi";
        });
        var deployment = VelocityProxyBuilder.BuildDeployment(cluster);

        var container = deployment.Spec.Template.Spec.Containers[0];
        await Assert.That(container.Resources.Requests.ContainsKey("cpu")).IsTrue();
        await Assert.That(container.Resources.Requests.ContainsKey("memory")).IsTrue();
    }

    [Test]
    public async Task BuildDeployment_HasProxyLabels()
    {
        var cluster = BuildCluster();
        var deployment = VelocityProxyBuilder.BuildDeployment(cluster);

        await Assert.That(deployment.Metadata.Labels["app.kubernetes.io/name"]).IsEqualTo("velocity-proxy");
        await Assert.That(deployment.Metadata.Labels["app.kubernetes.io/component"]).IsEqualTo("proxy");
        await Assert.That(deployment.Metadata.Labels["mc-operator.dhv.sh/cluster-name"]).IsEqualTo("test-cluster");
    }

    // ========== Service Tests ==========

    [Test]
    public async Task BuildService_HasCorrectName()
    {
        var cluster = BuildCluster();
        var service = VelocityProxyBuilder.BuildService(cluster);

        await Assert.That(service.Metadata.Name).IsEqualTo("test-cluster-proxy");
    }

    [Test]
    public async Task BuildService_HasOwnerReference()
    {
        var cluster = BuildCluster();
        var service = VelocityProxyBuilder.BuildService(cluster);

        await Assert.That(service.Metadata.OwnerReferences.Count).IsEqualTo(1);
        await Assert.That(service.Metadata.OwnerReferences[0].Kind).IsEqualTo("MinecraftServerCluster");
    }

    [Test]
    public async Task BuildService_DefaultsToClusterIP()
    {
        var cluster = BuildCluster();
        var service = VelocityProxyBuilder.BuildService(cluster);

        await Assert.That(service.Spec.Type).IsEqualTo("ClusterIP");
    }

    [Test]
    public async Task BuildService_CanBeLoadBalancer()
    {
        var cluster = BuildCluster(s => s.Proxy.Service.Type = ServiceType.LoadBalancer);
        var service = VelocityProxyBuilder.BuildService(cluster);

        await Assert.That(service.Spec.Type).IsEqualTo("LoadBalancer");
    }

    [Test]
    public async Task BuildService_CanBeNodePort()
    {
        var cluster = BuildCluster(s =>
        {
            s.Proxy.Service.Type = ServiceType.NodePort;
            s.Proxy.Service.NodePort = 30577;
        });
        var service = VelocityProxyBuilder.BuildService(cluster);

        await Assert.That(service.Spec.Type).IsEqualTo("NodePort");
        await Assert.That(service.Spec.Ports[0].NodePort).IsEqualTo(30577);
    }

    [Test]
    public async Task BuildService_HasProxyPort()
    {
        var cluster = BuildCluster(s => s.Proxy.ProxyPort = 25577);
        var service = VelocityProxyBuilder.BuildService(cluster);

        await Assert.That(service.Spec.Ports[0].Port).IsEqualTo(25577);
    }

    [Test]
    public async Task BuildService_HasAnnotations_WhenSpecified()
    {
        var cluster = BuildCluster(s =>
        {
            s.Proxy.Service.Annotations["service.beta.kubernetes.io/aws-load-balancer-type"] = "nlb";
        });
        var service = VelocityProxyBuilder.BuildService(cluster);

        await Assert.That(service.Metadata.Annotations!.ContainsKey(
            "service.beta.kubernetes.io/aws-load-balancer-type")).IsTrue();
    }

    // ========== ConfigMap Tests ==========

    [Test]
    public async Task BuildConfigMap_HasCorrectName()
    {
        var cluster = BuildCluster();
        var addresses = BuildServerAddresses();
        var cm = VelocityProxyBuilder.BuildConfigMap(cluster, addresses, "test-secret");

        await Assert.That(cm.Metadata.Name).IsEqualTo("test-cluster-proxy-config");
    }

    [Test]
    public async Task BuildConfigMap_HasOwnerReference()
    {
        var cluster = BuildCluster();
        var addresses = BuildServerAddresses();
        var cm = VelocityProxyBuilder.BuildConfigMap(cluster, addresses, "test-secret");

        await Assert.That(cm.Metadata.OwnerReferences.Count).IsEqualTo(1);
        await Assert.That(cm.Metadata.OwnerReferences[0].Kind).IsEqualTo("MinecraftServerCluster");
    }

    [Test]
    public async Task BuildConfigMap_ContainsVelocityToml()
    {
        var cluster = BuildCluster();
        var addresses = BuildServerAddresses();
        var cm = VelocityProxyBuilder.BuildConfigMap(cluster, addresses, "test-secret");

        await Assert.That(cm.Data.ContainsKey("velocity.toml")).IsTrue();
        await Assert.That(cm.Data["velocity.toml"]).Contains("config-version");
    }

    [Test]
    public async Task BuildConfigMap_ContainsForwardingSecret()
    {
        var cluster = BuildCluster();
        var addresses = BuildServerAddresses();
        var cm = VelocityProxyBuilder.BuildConfigMap(cluster, addresses, "my-secret-value");

        await Assert.That(cm.Data.ContainsKey("forwarding.secret")).IsTrue();
        await Assert.That(cm.Data["forwarding.secret"]).IsEqualTo("my-secret-value");
    }

    // ========== Velocity TOML Tests ==========

    [Test]
    public async Task BuildVelocityToml_ContainsBindAddress()
    {
        var cluster = BuildCluster(s => s.Proxy.ProxyPort = 25577);
        var addresses = BuildServerAddresses();
        var toml = VelocityProxyBuilder.BuildVelocityToml(cluster, addresses);

        await Assert.That(toml).Contains("bind = \"0.0.0.0:25577\"");
    }

    [Test]
    public async Task BuildVelocityToml_ContainsMotd()
    {
        var cluster = BuildCluster(s => s.Proxy.Motd = "Welcome to my cluster");
        var addresses = BuildServerAddresses();
        var toml = VelocityProxyBuilder.BuildVelocityToml(cluster, addresses);

        await Assert.That(toml).Contains("motd = \"Welcome to my cluster\"");
    }

    [Test]
    public async Task BuildVelocityToml_ContainsDefaultMotd_WhenNotSpecified()
    {
        var cluster = BuildCluster(s => s.Proxy.Motd = null);
        var addresses = BuildServerAddresses();
        var toml = VelocityProxyBuilder.BuildVelocityToml(cluster, addresses);

        await Assert.That(toml).Contains("motd = \"A Minecraft Cluster (test-cluster)\"");
    }

    [Test]
    public async Task BuildVelocityToml_ContainsMaxPlayers()
    {
        var cluster = BuildCluster(s => s.Proxy.MaxPlayers = 200);
        var addresses = BuildServerAddresses();
        var toml = VelocityProxyBuilder.BuildVelocityToml(cluster, addresses);

        await Assert.That(toml).Contains("show-max-players = 200");
    }

    [Test]
    public async Task BuildVelocityToml_ContainsOnlineMode()
    {
        var cluster = BuildCluster(s => s.Proxy.OnlineMode = true);
        var addresses = BuildServerAddresses();
        var toml = VelocityProxyBuilder.BuildVelocityToml(cluster, addresses);

        await Assert.That(toml).Contains("online-mode = true");
    }

    [Test]
    public async Task BuildVelocityToml_ContainsForwardingMode()
    {
        var cluster = BuildCluster(s => s.Proxy.PlayerForwardingMode = PlayerForwardingMode.Modern);
        var addresses = BuildServerAddresses();
        var toml = VelocityProxyBuilder.BuildVelocityToml(cluster, addresses);

        await Assert.That(toml).Contains("player-info-forwarding-mode = \"modern\"");
    }

    [Test]
    public async Task BuildVelocityToml_RegistersAllServers()
    {
        var cluster = BuildCluster();
        var addresses = BuildServerAddresses(3);
        var toml = VelocityProxyBuilder.BuildVelocityToml(cluster, addresses);

        await Assert.That(toml).Contains("server-0 = \"test-cluster-0:25565\"");
        await Assert.That(toml).Contains("server-1 = \"test-cluster-1:25565\"");
        await Assert.That(toml).Contains("server-2 = \"test-cluster-2:25565\"");
    }

    [Test]
    public async Task BuildVelocityToml_HasTryOrder_DefaultAscending()
    {
        var cluster = BuildCluster();
        var addresses = BuildServerAddresses(3);
        var toml = VelocityProxyBuilder.BuildVelocityToml(cluster, addresses);

        await Assert.That(toml).Contains("try = [\"server-0\", \"server-1\", \"server-2\"]");
    }

    [Test]
    public async Task BuildVelocityToml_HasTryOrder_CustomOrder()
    {
        var cluster = BuildCluster(s => s.Proxy.TryOrder = new List<int> { 2, 0, 1 });
        var addresses = BuildServerAddresses(3);
        var toml = VelocityProxyBuilder.BuildVelocityToml(cluster, addresses);

        await Assert.That(toml).Contains("try = [\"server-2\", \"server-0\", \"server-1\"]");
    }

    [Test]
    public async Task BuildVelocityToml_HasAdvancedSettings()
    {
        var cluster = BuildCluster();
        var addresses = BuildServerAddresses();
        var toml = VelocityProxyBuilder.BuildVelocityToml(cluster, addresses);

        await Assert.That(toml).Contains("[advanced]");
        await Assert.That(toml).Contains("compression-threshold = 256");
        await Assert.That(toml).Contains("failover-on-unexpected-server-disconnect = true");
    }

    // ========== Helper Tests ==========

    [Test]
    public async Task ResolveImage_ReturnsDefaultImage_WhenNoImageSpecified()
    {
        var spec = new VelocityProxySpec();
        var image = VelocityProxyBuilder.ResolveImage(spec);

        await Assert.That(image).IsEqualTo("itzg/mc-proxy:latest");
    }

    [Test]
    public async Task ResolveImage_ReturnsCustomImage_WhenImageSpecified()
    {
        var spec = new VelocityProxySpec { Image = "my-proxy:custom" };
        var image = VelocityProxyBuilder.ResolveImage(spec);

        await Assert.That(image).IsEqualTo("my-proxy:custom");
    }

    [Test]
    public async Task GenerateForwardingSecret_ReturnsNonEmptyString()
    {
        var secret = VelocityProxyBuilder.GenerateForwardingSecret();

        await Assert.That(string.IsNullOrEmpty(secret)).IsFalse();
    }

    [Test]
    public async Task GenerateForwardingSecret_ReturnsUniqueValues()
    {
        var secret1 = VelocityProxyBuilder.GenerateForwardingSecret();
        var secret2 = VelocityProxyBuilder.GenerateForwardingSecret();

        await Assert.That(secret1).IsNotEqualTo(secret2);
    }
}
