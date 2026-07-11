using Melodee.Common.Models.Scrobbling;

namespace Melodee.Common.Models.OpenSubsonic.Requests;

/// <summary>
///     Setup from Query/Post parameters for the Subsonic request.
/// </summary>
/// <param name="RequestHeaders">All request headers for request.</param>
/// <param name="RequiresAuthentication">If false then an internal request from the Blazor UI else an API call.</param>
/// <param name="Username">(u) The username.</param>
/// <param name="Version">
///     (v) The protocol version implemented by the client, i.e., the version of the
///     subsonic-rest-api.xsd schema used
/// </param>
/// <param name="Format">(f) Request data to be returned in this format.</param>
/// <param name="ApiKey">An API key used for authentication</param>
/// <param name="Password">(p) The password, either in clear text or hex-encoded with a “enc:” prefix.</param>
/// <param name="Token">(t) The authentication token computed as md5(password + salt).</param>
/// <param name="Salt">(s) A random string (“salt”) used as input for computing the password hash</param>
/// <param name="Callback">(callback) Callback name to use with jsonp requests.</param>
/// <param name="Jwt">(jwt) JsonWebToken used in auth, seen in the Navidrome source code.</param>
/// <param name="ApiRequestPlayer">Details on the request subsonic client application (aka player.)</param>
/// <param name="IpAddress">The IP address of the client making the request.</param>
public record ApiRequest(
    KeyValue[] RequestHeaders,
    bool RequiresAuthentication,
    string? Username,
    string? Version,
    string? Format,
    string? ApiKey,
    string? Password,
    string? Token,
    string? Salt,
    string? Callback,
    string? Jwt,
    UserPlayer ApiRequestPlayer,
    string? IpAddress = null)
{
    public bool IsJsonRequest => string.Equals(Format, "json", StringComparison.OrdinalIgnoreCase);

    public bool IsJsonPRequest => string.Equals(Format, "jsonp", StringComparison.OrdinalIgnoreCase) &&
                                  !string.IsNullOrWhiteSpace(Callback);

    /// <summary>
    ///     Defaults to XML
    /// </summary>
    public bool IsXmlRequest => !IsJsonRequest && !IsJsonPRequest;

    public bool IsAuthorized => Username != null && ApiKey != null;
}
