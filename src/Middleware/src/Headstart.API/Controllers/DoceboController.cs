using Headstart.Models.Attributes;
using Microsoft.AspNetCore.Mvc;
using ordercloud.integrations.docebo;
using ordercloud.integrations.docebo.Models;
using ordercloud.integrations.library;
using OrderCloud.Catalyst;
using OrderCloud.SDK;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Headstart.API.Controllers
{
    /// <summary>
    /// Docebo LMS for Headstart
    /// </summary>
    [Route("docebo")]
    public class DoceboController : CatalystController
    {
        private readonly IOrderCloudIntegrationsDoceboService _docebo;
        private readonly IOrderCloudClient _oc;
        public DoceboController(IOrderCloudIntegrationsDoceboService docebo, IOrderCloudClient oc)
        {
            _docebo = docebo;
            _oc = oc;
        }

        /// <summary>
        /// POST PaymentIntentResponse
        /// </summary>
        //[HttpPost, Route("posttoken"), OrderCloudUserAuth(ApiRole.Shopper)]
        //public async Task<DoceboToken> Post([FromBody] DoceboToken request)
        //{
        //    return await _docebo.GetToken();
        //}

        /// <summary>
        /// Returns whether a Docebo account exists for the given email.
        /// No PII is included in the response.
        /// </summary>
        [HttpGet, Route("{email}"), OrderCloudUserAuth(ApiRole.Shopper)]
        public async Task<DoceboUserExistsResponse> ListDoceboUsers(string email)
        {
            var currentUser = await _oc.Me.GetAsync(accessToken: UserContext.AccessToken);
            var callerDomain = currentUser.Email.Split('@').Last();
            var requestedDomain = email.Split('@').Last();

            Require.That(
                callerDomain.Equals(requestedDomain, System.StringComparison.OrdinalIgnoreCase),
                new ErrorCode("Insufficient Access", "You may only search for users within your own email domain.", HttpStatusCode.Forbidden)
            );

            var result = await _docebo.SearchUsers(email);
            return new DoceboUserExistsResponse
            {
                exists = result?.data?.items?.Any() ?? false
            };
        }
    }
}
