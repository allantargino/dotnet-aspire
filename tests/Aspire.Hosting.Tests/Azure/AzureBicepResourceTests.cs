// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Sockets;
using System.Text.Json.Nodes;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.Postgres;
using Aspire.Hosting.Utils;
using Xunit;

namespace Aspire.Hosting.Tests.Azure;

public class AzureBicepResourceTests
{
    [Fact]
    public void AddBicepResource()
    {
        var builder = DistributedApplication.CreateBuilder();

        var bicepResource = builder.AddBicepTemplateString("mytemplate", "content")
                                   .WithParameter("param1", "value1")
                                   .WithParameter("param2", "value2");

        Assert.Equal("content", bicepResource.Resource.TemplateString);
        Assert.Equal("value1", bicepResource.Resource.Parameters["param1"]);
        Assert.Equal("value2", bicepResource.Resource.Parameters["param2"]);
    }

    [Fact]
    public void GetOutputReturnsOutputValue()
    {
        var builder = DistributedApplication.CreateBuilder();

        var bicepResource = builder.AddBicepTemplateString("templ", "content");

        bicepResource.Resource.Outputs["resourceEndpoint"] = "https://myendpoint";

        Assert.Equal("https://myendpoint", bicepResource.GetOutput("resourceEndpoint").Value);
    }

    [Fact]
    public void GetSecretOutputReturnsSecretOutputValue()
    {
        var builder = DistributedApplication.CreateBuilder();

        var bicepResource = builder.AddBicepTemplateString("templ", "content");

        bicepResource.Resource.SecretOutputs["connectionString"] = "https://myendpoint;Key=43";

        Assert.Equal("https://myendpoint;Key=43", bicepResource.GetSecretOutput("connectionString").Value);
    }

    [Fact]
    public void GetOutputValueThrowsIfNoOutput()
    {
        var builder = DistributedApplication.CreateBuilder();

        var bicepResource = builder.AddBicepTemplateString("templ", "content");

        Assert.Throws<InvalidOperationException>(() => bicepResource.GetOutput("resourceEndpoint").Value);
    }

    [Fact]
    public void GetSecretOutputValueThrowsIfNoOutput()
    {
        var builder = DistributedApplication.CreateBuilder();

        var bicepResource = builder.AddBicepTemplateString("templ", "content");

        Assert.Throws<InvalidOperationException>(() => bicepResource.GetSecretOutput("connectionString").Value);
    }

    [Fact]
    public void AssertManifestLayout()
    {
        var builder = DistributedApplication.CreateBuilder();

        var param = builder.AddParameter("p1");

        var bicepResource = builder.AddBicepTemplateString("templ", "content")
                                    .WithParameter("param1", "value1")
                                    .WithParameter("param2", ["1", "2"])
                                    .WithParameter("param3", new JsonObject() { ["value"] = "nested" })
                                    .WithParameter("param4", param);

        // This makes a temp file
        var obj = ManifestUtils.GetManifest(bicepResource.Resource.WriteToManifest);

        Assert.NotNull(obj);
        Assert.Equal("azure.bicep.v0", obj["type"]?.ToString());
        Assert.NotNull(obj["path"]?.ToString());
        var parameters = obj["params"];
        Assert.NotNull(parameters);
        Assert.Equal("value1", parameters?["param1"]?.ToString());
        Assert.Equal("1", parameters?["param2"]?[0]?.ToString());
        Assert.Equal("2", parameters?["param2"]?[1]?.ToString());
        Assert.Equal("nested", parameters?["param3"]?["value"]?.ToString());
        Assert.Equal($"{{{param.Resource.Name}.value}}", parameters?["param4"]?.ToString());
    }

    [Fact]
    public void AddBicepCosmosDb()
    {
        var builder = DistributedApplication.CreateBuilder();

        var cosmos = builder.AddBicepCosmosDb("cosmos");
        cosmos.AddDatabase("db", "mydatabase");

        cosmos.Resource.SecretOutputs["connectionString"] = "mycosmosconnectionstring";

        var databases = cosmos.Resource.Parameters["databases"] as IEnumerable<string>;

        Assert.Equal("Aspire.Hosting.Azure.Bicep.cosmosdb.bicep", cosmos.Resource.TemplateResourceName);
        Assert.Equal("cosmos", cosmos.Resource.Name);
        Assert.Equal("cosmos", cosmos.Resource.Parameters["databaseAccountName"]);
        Assert.NotNull(databases);
        Assert.Equal(["mydatabase"], databases);
        Assert.Equal("mycosmosconnectionstring", cosmos.Resource.GetConnectionString());
        Assert.Equal("{cosmos.secretOutputs.connectionString}", cosmos.Resource.ConnectionStringExpression);
    }

    [Fact]
    public void AddBicepCosmosDbDatabaseReferencesParentConnectionString()
    {
        var builder = DistributedApplication.CreateBuilder();

        var db = builder.AddBicepCosmosDb("cosmos").AddDatabase("db", "mydatabase");

        var obj = ManifestUtils.GetManifest(db.Resource.WriteToManifest);

        Assert.NotNull(obj);
        Assert.Equal("azure.bicep.v0", obj["type"]?.ToString());
        Assert.Equal("{cosmos.connectionString}", obj["connectionString"]?.ToString());
        Assert.Equal("cosmos", obj["parent"]?.ToString());
    }

    [Fact]
    public void AddAppConfiguration()
    {
        var builder = DistributedApplication.CreateBuilder();

        var appConfig = builder.AddAzureAppConfiguration("appConfig");

        appConfig.Resource.Outputs["appConfigEndpoint"] = "https://myendpoint";

        Assert.Equal("Aspire.Hosting.Azure.Bicep.appconfig.bicep", appConfig.Resource.TemplateResourceName);
        Assert.Equal("appConfig", appConfig.Resource.Name);
        Assert.Equal("appconfig", appConfig.Resource.Parameters["configName"]);
        Assert.Equal("https://myendpoint", appConfig.Resource.GetConnectionString());
        Assert.Equal("{appConfig.outputs.appConfigEndpoint}", appConfig.Resource.ConnectionStringExpression);
    }

    [Fact]
    public void AddApplicationInsights()
    {
        var builder = DistributedApplication.CreateBuilder();

        var appInsights = builder.AddAzureApplicationInsights("appInsights");

        appInsights.Resource.Outputs["appInsightsConnectionString"] = "myinstrumentationkey";

        Assert.Equal("Aspire.Hosting.Azure.Bicep.appinsights.bicep", appInsights.Resource.TemplateResourceName);
        Assert.Equal("appInsights", appInsights.Resource.Name);
        Assert.Equal("appinsights", appInsights.Resource.Parameters["appInsightsName"]);
        Assert.Equal("myinstrumentationkey", appInsights.Resource.GetConnectionString());
        Assert.Equal("{appInsights.outputs.appInsightsConnectionString}", appInsights.Resource.ConnectionStringExpression);
    }

    [Fact]
    public void WithReferenceAppInsightsSetsEnvironmentVariable()
    {
        var builder = DistributedApplication.CreateBuilder();

        var appInsights = builder.AddAzureApplicationInsights("ai");

        appInsights.Resource.Outputs["appInsightsConnectionString"] = "myinstrumentationkey";

        var serviceA = builder.AddProject<Projects.ServiceA>("serviceA")
            .WithReference(appInsights);

        // Call environment variable callbacks.
        var annotations = serviceA.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>();

        var config = new Dictionary<string, string>();
        var executionContext = new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run);
        var context = new EnvironmentCallbackContext(executionContext, config);

        foreach (var annotation in annotations)
        {
            annotation.Callback(context);
        }

        Assert.True(config.ContainsKey("APPLICATIONINSIGHTS_CONNECTION_STRING"));
        Assert.Equal("myinstrumentationkey", config["APPLICATIONINSIGHTS_CONNECTION_STRING"]);
    }

    [Fact]
    public void PublishAsRedisPublishesRedisAsAzureRedis()
    {
        var builder = DistributedApplication.CreateBuilder();

        var redis = builder.AddRedis("cache")
            .WithAnnotation(new AllocatedEndpointAnnotation("tcp", ProtocolType.Tcp, "localhost", 12455, "tcp"))
            .PublishAsAzureRedis();

        Assert.True(redis.Resource.IsContainer());

        Assert.Equal("localhost:12455", redis.Resource.GetConnectionString());

        var manifest = ManifestUtils.GetManifest(redis.Resource);

        Assert.Equal("azure.bicep.v0", manifest["type"]?.ToString());
        Assert.Equal("{cache.secretOutputs.connectionString}", manifest["connectionString"]?.ToString());
    }

    [Fact]
    public void AddBicepKeyVault()
    {
        var builder = DistributedApplication.CreateBuilder();

        var keyVault = builder.AddAzureKeyVault("keyVault");

        keyVault.Resource.Outputs["vaultUri"] = "https://myvault";

        Assert.Equal("Aspire.Hosting.Azure.Bicep.keyvault.bicep", keyVault.Resource.TemplateResourceName);
        Assert.Equal("keyVault", keyVault.Resource.Name);
        Assert.Equal("keyvault", keyVault.Resource.Parameters["vaultName"]);
        Assert.Equal("https://myvault", keyVault.Resource.GetConnectionString());
        Assert.Equal("{keyVault.outputs.vaultUri}", keyVault.Resource.ConnectionStringExpression);
    }

    [Fact]
    public void AddBicepSqlServer()
    {
        var builder = DistributedApplication.CreateBuilder();

        var sql = builder.AddBicepAzureSqlServer("sql");
        sql.AddDatabase("db", "database");

        sql.Resource.Outputs["sqlServerFqdn"] = "myserver";

        var databases = sql.Resource.Parameters["databases"] as IEnumerable<string>;

        Assert.Equal("Aspire.Hosting.Azure.Bicep.sql.bicep", sql.Resource.TemplateResourceName);
        Assert.Equal("sql", sql.Resource.Name);
        Assert.Equal("sql", sql.Resource.Parameters["serverName"]);
        Assert.NotNull(databases);
        Assert.Equal(["database"], databases);
        Assert.Equal("Server=tcp:myserver,1433;Encrypt=True;Authentication=\"Active Directory Default\"", sql.Resource.GetConnectionString());
        Assert.Equal("Server=tcp:{sql.outputs.sqlServerFqdn},1433;Encrypt=True;Authentication=\"Active Directory Default\"", sql.Resource.ConnectionStringExpression);
    }

    [Fact]
    public void AsAzurePostgresFlexibleServer()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.Configuration["Parameters:usr"] = "user";
        builder.Configuration["Parameters:pwd"] = "password";

        var usr = builder.AddParameter("usr");
        var pwd = builder.AddParameter("pwd", secret: true);

        IResourceBuilder<AzurePostgresResource>? azurePostgres = null;
        var postgres = builder.AddPostgres("postgres").AsAzurePostgresFlexibleServer(usr, pwd, (resource) =>
        {
            Assert.NotNull(resource);
            azurePostgres = resource;
        });
        postgres.AddDatabase("db");

        Assert.NotNull(azurePostgres);

        var databasesCallback = azurePostgres.Resource.Parameters["databases"] as Func<object?>;
        Assert.NotNull(databasesCallback);
        var databases = databasesCallback() as IEnumerable<string>;

        Assert.Equal("Aspire.Hosting.Azure.Bicep.postgres.bicep", azurePostgres.Resource.TemplateResourceName);
        Assert.Equal("postgres", postgres.Resource.Name);
        Assert.Equal("postgres", azurePostgres.Resource.Parameters["serverName"]);
        Assert.Same(usr, azurePostgres.Resource.Parameters["administratorLogin"]);
        Assert.Same(pwd, azurePostgres.Resource.Parameters["administratorLoginPassword"]);
        Assert.True(azurePostgres.Resource.Parameters.ContainsKey(AzureBicepResource.KnownParameters.KeyVaultName));
        Assert.NotNull(databases);
        Assert.Equal(["db"], databases);

        // Setup to verify that connection strings is acquired via resource connectionstring redirct.
        azurePostgres.Resource.SecretOutputs["connectionString"] = "myconnectionstring";
        Assert.Equal("myconnectionstring", postgres.Resource.GetConnectionString());

        Assert.Equal("{postgres.secretOutputs.connectionString}", azurePostgres.Resource.ConnectionStringExpression);
    }

    [Fact]
    public void PublishAsAzurePostgresFlexibleServer()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.Configuration["Parameters:usr"] = "user";
        builder.Configuration["Parameters:pwd"] = "password";

        var usr = builder.AddParameter("usr");
        var pwd = builder.AddParameter("pwd", secret: true);

        IResourceBuilder<AzurePostgresResource>? azurePostgres = null;
        var postgres = builder.AddPostgres("postgres").PublishAsAzurePostgresFlexibleServer(usr, pwd, (resource) =>
        {
            azurePostgres = resource;
        });
        postgres.AddDatabase("db");

        Assert.NotNull(azurePostgres);

        var databasesCallback = azurePostgres.Resource.Parameters["databases"] as Func<object?>;
        Assert.NotNull(databasesCallback);
        var databases = databasesCallback() as IEnumerable<string>;

        Assert.Equal("Aspire.Hosting.Azure.Bicep.postgres.bicep", azurePostgres.Resource.TemplateResourceName);
        Assert.Equal("postgres", postgres.Resource.Name);
        Assert.Equal("postgres", azurePostgres.Resource.Parameters["serverName"]);
        Assert.Same(usr, azurePostgres.Resource.Parameters["administratorLogin"]);
        Assert.Same(pwd, azurePostgres.Resource.Parameters["administratorLoginPassword"]);
        Assert.True(azurePostgres.Resource.Parameters.ContainsKey(AzureBicepResource.KnownParameters.KeyVaultName));
        Assert.NotNull(databases);
        Assert.Equal(["db"], databases);

        // Verify that when PublishAs variant is used, connection string acquisition
        // still uses the local endpoint.
        var endpointAnnotation = new AllocatedEndpointAnnotation("dummy", System.Net.Sockets.ProtocolType.Tcp, "localhost", 1234, "tcp");
        postgres.WithAnnotation(endpointAnnotation);
        var expectedConnectionString = $"Host={endpointAnnotation.Address};Port={endpointAnnotation.Port};Username=postgres;Password={PasswordUtil.EscapePassword(postgres.Resource.Password)}";
        Assert.Equal(expectedConnectionString, postgres.Resource.GetConnectionString());

        Assert.Equal("{postgres.secretOutputs.connectionString}", azurePostgres.Resource.ConnectionStringExpression);

        var manifest = ManifestUtils.GetManifest(postgres.Resource);

        Assert.Equal("azure.bicep.v0", manifest["type"]?.ToString());
        Assert.Equal("{postgres.secretOutputs.connectionString}", manifest["connectionString"]?.ToString());
    }

    [Fact]
    public void AddBicepServiceBus()
    {
        var builder = DistributedApplication.CreateBuilder();

        var sb = AzureServiceBusExtensions.AddAzureServiceBus(builder, "sb", ["queue1"], ["topic1"]);

        sb.Resource.Outputs["serviceBusEndpoint"] = "mynamespaceEndpoint";

        var queues = sb.Resource.Parameters["queues"] as IEnumerable<string>;
        var topics = sb.Resource.Parameters["topics"] as IEnumerable<string>;

        Assert.Equal("Aspire.Hosting.Azure.Bicep.servicebus.bicep", sb.Resource.TemplateResourceName);
        Assert.Equal("sb", sb.Resource.Name);
        Assert.Equal("sb", sb.Resource.Parameters["serviceBusNamespaceName"]);
        Assert.NotNull(queues);
        Assert.Equal(["queue1"], queues);
        Assert.NotNull(topics);
        Assert.Equal(["topic1"], topics);
        Assert.Equal("mynamespaceEndpoint", sb.Resource.GetConnectionString());
        Assert.Equal("{sb.outputs.serviceBusEndpoint}", sb.Resource.ConnectionStringExpression);
    }

    [Fact]
    public void AddBicepStorage()
    {
        var builder = DistributedApplication.CreateBuilder();

        var storage = builder.AddAzureStorage("storage");

        storage.Resource.Outputs["blobEndpoint"] = "https://myblob";
        storage.Resource.Outputs["queueEndpoint"] = "https://myqueue";
        storage.Resource.Outputs["tableEndpoint"] = "https://mytable";

        var blob = storage.AddBlobs("blob");
        var queue = storage.AddQueues("queue");
        var table = storage.AddTables("table");

        Assert.Equal("Aspire.Hosting.Azure.Bicep.storage.bicep", storage.Resource.TemplateResourceName);
        Assert.Equal("storage", storage.Resource.Name);
        Assert.Equal("storage", storage.Resource.Parameters["storageName"]);

        Assert.Equal("https://myblob", blob.Resource.GetConnectionString());
        Assert.Equal("https://myqueue", queue.Resource.GetConnectionString());
        Assert.Equal("https://mytable", table.Resource.GetConnectionString());
        Assert.Equal("{storage.outputs.blobEndpoint}", blob.Resource.ConnectionStringExpression);
        Assert.Equal("{storage.outputs.queueEndpoint}", queue.Resource.ConnectionStringExpression);
        Assert.Equal("{storage.outputs.tableEndpoint}", table.Resource.ConnectionStringExpression);

        var blobManifest = ManifestUtils.GetManifest(blob.Resource);
        Assert.Equal("{storage.outputs.blobEndpoint}", blobManifest["connectionString"]?.ToString());

        var queueManifest = ManifestUtils.GetManifest(queue.Resource);
        Assert.Equal("{storage.outputs.queueEndpoint}", queueManifest["connectionString"]?.ToString());

        var tableManifest = ManifestUtils.GetManifest(table.Resource);
        Assert.Equal("{storage.outputs.tableEndpoint}", tableManifest["connectionString"]?.ToString());
    }
}
