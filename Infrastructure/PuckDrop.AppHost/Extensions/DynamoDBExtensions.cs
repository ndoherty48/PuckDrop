namespace PuckDrop.AppHost.Extensions;

public static class DynamoDBExtensions
{
    public static IResourceBuilder<T> WithAWSLocalCredentials<T>(this IResourceBuilder<T>builder ) where T : IResourceWithEnvironment
    {
        if(builder.ApplicationBuilder.ExecutionContext.IsRunMode is false) 
            return builder;

        return builder
            .WithEnvironment("AWS_REGION", "eu-west-1")
            .WithEnvironment("AWS_ACCESS_KEY_ID", "local")
            .WithEnvironment("AWS_SECRET_ACCESS_KEY", "local");
    }

    extension(IDistributedApplicationBuilder builder)
    {


        public object[] GetDynamoDbResourceParams(EndpointReference dynamoDbLocalEndpoint)
        {
            return [
                "dynamodb", "create-table",
                "--endpoint-url", dynamoDbLocalEndpoint,
                "--table-name", "PuckDrop",
                "--billing-mode", "PAY_PER_REQUEST",
                "--attribute-definitions",
                    "AttributeName=PK,AttributeType=S",
                    "AttributeName=SK,AttributeType=S",
                    "AttributeName=GSI1PK,AttributeType=S",
                    "AttributeName=GSI1SK,AttributeType=S",
                    "AttributeName=GSI2PK,AttributeType=S",
                    "AttributeName=GSI2SK,AttributeType=S",
                "--key-schema",
                    "AttributeName=PK,KeyType=HASH",
                    "AttributeName=SK,KeyType=RANGE",
                "--global-secondary-indexes",
                    """[{"IndexName":"GSI1","KeySchema":[{"AttributeName":"GSI1PK","KeyType":"HASH"},{"AttributeName":"GSI1SK","KeyType":"RANGE"}],"Projection":{"ProjectionType":"ALL"}},{"IndexName":"GSI2","KeySchema":[{"AttributeName":"GSI2PK","KeyType":"HASH"},{"AttributeName":"GSI2SK","KeyType":"RANGE"}],"Projection":{"ProjectionType":"ALL"}}]"""
            ];
        }
    }
}