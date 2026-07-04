namespace PuckDrop.AppHost.Extensions;

public static class DynamoDBExtensions
{
    extension(IDistributedApplicationBuilder builder)
    {
        public object[] GetDynamoDbResourceParams(EndpointReference dynamoDbLocalEndpoint)
        {
            return [
                "dynamodb", "create-table",
                "--endpoint-url", dynamoDbLocalEndpoint,
                "--table-name", "PuckDrop",
                "--billing-mode", "PAY_PER_REQUEST",
                "--region", "eu-west-1",
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