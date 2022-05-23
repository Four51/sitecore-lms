﻿using System.Threading.Tasks;
using Headstart.Models.Attributes;
using Microsoft.AspNetCore.Mvc;
using ordercloud.integrations.docebo;
using ordercloud.integrations.library;
using OrderCloud.Catalyst;
using OrderCloud.SDK;
using ordercloud.integrations.docebo.Models;

namespace Headstart.API.Controllers
{
    /// <summary>
    /// Docebo LMS for Headstart
    /// </summary>
    [Route("docebo")]
    public class DoceboController : CatalystController
    {
        private readonly IOrderCloudIntegrationsDoceboService _docebo;
        public DoceboController(IOrderCloudIntegrationsDoceboService docebo)
        {
            _docebo = docebo;
        }

        /// <summary>
        /// POST PaymentIntentResponse
        /// </summary>
        [HttpPost, Route("posttoken"), OrderCloudUserAuth(ApiRole.Shopper)]
        public async Task<DoceboToken> Post([FromBody] DoceboToken request)
        {
            return await _docebo.GetToken();
        }

        /// <summary>
        /// GET Docebo Token For Local Testing Only
        /// </summary>
        [HttpGet, Route("gettoken")]
        public async Task<DoceboToken> GetToken()
        {
            return await _docebo.GetToken();
        }
    }
}
