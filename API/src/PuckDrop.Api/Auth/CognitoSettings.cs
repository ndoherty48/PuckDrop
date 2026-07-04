namespace PuckDrop.Api.Auth;

/// <summary>
/// Configuration settings for Amazon Cognito authentication.
/// Bound from appsettings "Cognito" section.
/// </summary>
public class CognitoSettings
{
    public const string SectionName = "Cognito";

    /// <summary>
    /// Cognito User Pool ID (e.g. "eu-west-1_abc123").
    /// </summary>
    public required string UserPoolId { get; set; }

    /// <summary>
    /// Cognito App Client ID.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// AWS Region where the User Pool is hosted (e.g. "eu-west-1").
    /// </summary>
    public required string Region { get; set; }

    /// <summary>
    /// The Cognito issuer URL, derived from Region and UserPoolId.
    /// </summary>
    public string Authority => $"https://cognito-idp.{Region}.amazonaws.com/{UserPoolId}";

    /// <summary>
    /// The name of the Cognito group that grants admin access.
    /// </summary>
    public string AdminGroupName { get; set; } = "admin";
}
