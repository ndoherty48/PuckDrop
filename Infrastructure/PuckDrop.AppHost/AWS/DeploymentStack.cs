using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Aspire.Hosting.AWS.Deployment;
using Constructs;
using Attribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace PuckDrop.AppHost.AWS;

public class DeploymentStack : Stack
{
    public DeploymentStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
    {
        // Use the account's default VPC instead of creating a new one
        DefaultVpc = Vpc.FromLookup(this, "DefaultVpc", new VpcLookupOptions
        {
            IsDefault = true
        });
        
        // Create a custom ECS cluster with specific configuration
        DefaultECSCluster = new Cluster(this, "MyCluster", new ClusterProps
        {
            Vpc = DefaultVpc,
            ClusterName = "my-aspire-cluster"
        });

        // DynamoDB single-table for PuckDrop
        PuckDropTable = new Table(this, "PuckDropTable", new TableProps
        {
            TableName = "PuckDrop",
            BillingMode = BillingMode.PAY_PER_REQUEST,
            RemovalPolicy = RemovalPolicy.RETAIN,
            PartitionKey = new Attribute { Name = "PK", Type = AttributeType.STRING },
            SortKey = new Attribute { Name = "SK", Type = AttributeType.STRING }
        });

        // GSI1 — Poll-centric queries (poll + questions + options, all answers for a poll)
        PuckDropTable.AddGlobalSecondaryIndex(new GlobalSecondaryIndexProps
        {
            IndexName = "GSI1",
            PartitionKey = new Attribute { Name = "GSI1PK", Type = AttributeType.STRING },
            SortKey = new Attribute { Name = "GSI1SK", Type = AttributeType.STRING },
            ProjectionType = ProjectionType.ALL
        });

        // GSI2 — Active poll lookup by season + status
        PuckDropTable.AddGlobalSecondaryIndex(new GlobalSecondaryIndexProps
        {
            IndexName = "GSI2",
            PartitionKey = new Attribute { Name = "GSI2PK", Type = AttributeType.STRING },
            SortKey = new Attribute { Name = "GSI2SK", Type = AttributeType.STRING },
            ProjectionType = ProjectionType.ALL
        });
    }

    [DefaultVpc]
    public IVpc DefaultVpc { get; private set; }
    
    [DefaultECSCluster]
    public ICluster DefaultECSCluster { get; private set; }

    public Table PuckDropTable { get; private set; }
}