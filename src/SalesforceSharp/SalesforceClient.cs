﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using HelperSharp;
using Newtonsoft.Json.Linq;
using RestSharp;
using SalesforceSharp.Security;
using SalesforceSharp.Serialization;

namespace SalesforceSharp
{
    /// <summary>
    /// The central point to communicate with Salesforce REST API.
    /// </summary>
    public class SalesforceClient
    {
        #region Fields
        private string m_accessToken;
        private DynamicJsonDeserializer m_deserializer;
        private IRestClient m_restClient;
        #endregion

        #region Properties
        /// <summary>
        /// Gets or sets the API version.
        /// </summary>
        /// <remarks>
        /// The default value is v28.0.
        /// </remarks>
        /// <value>
        /// The API version.
        /// </value>
        public string ApiVersion { get; set; }       

        /// <summary>
        /// Gets a value indicating whether this instance is authenticated.
        /// </summary>
        /// <value>
        /// <c>true</c> if this instance is authenticated; otherwise, <c>false</c>.
        /// </value>
        public bool IsAuthenticated { get; private set; }

        /// <summary>
        /// Gets the instance URL.
        /// </summary>
        /// <value>
        /// The instance URL.
        /// </value>
        public string InstanceUrl { get; private set; }
        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="SalesforceClient"/> class.
        /// </summary>        
        public SalesforceClient() : this(new RestClient())
        {
           
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SalesforceClient"/> class.
        /// </summary>
        /// <param name="restClient">The rest client.</param>
        internal SalesforceClient(IRestClient restClient)
        {
            m_restClient = restClient;
            ApiVersion = "v28.0";
            m_deserializer = new DynamicJsonDeserializer();
        }
        #endregion

        #region Methods
        /// <summary>
        /// Authenticates the client.
        /// </summary>
        /// <param name="authenticationFlow">The authentication flow which will be used to authenticate on REST API.</param>
        public void Authenticate(IAuthenticationFlow authenticationFlow)
        {
            var info = authenticationFlow.Authenticate();
            m_accessToken = info.AccessToken;
            InstanceUrl = info.InstanceUrl;
            IsAuthenticated = true;
        }
     
        /// <summary>
        /// Executes a SOQL query and returns the result.
        /// </summary>
        /// <param name="query">The SOQL query.</param>
        /// <returns>The API result for the query.</returns>
        public IList<T> Query<T>(string query) where T : new()
        {
            ExceptionHelper.ThrowIfNullOrEmpty("query", query);

            var url = "{0}?q={1}".With(GetUrl("query"), query);
            var response = Request<SalesforceQueryResult<T>>(url);

            return response.Data.Records;
        }

        /// <summary>
        /// Finds a record by Id.
        /// </summary>
        /// <typeparam name="T">The record type.</typeparam>
        /// <param name="objectName">The name of the object in Salesforce.</param>
        /// <param name="recordId">The record id.</param>
        /// <returns>The record with the specified id.</returns>
        public T FindById<T>(string objectName, string recordId) where T : new()
        {
            ExceptionHelper.ThrowIfNullOrEmpty("objectName", objectName);
            ExceptionHelper.ThrowIfNullOrEmpty("recordId", recordId);

            var result = Query<T>("SELECT {0} FROM {1} WHERE Id = '{2}'".With(GetRecordProjection(typeof(T)), objectName, recordId));

            return result.FirstOrDefault();
        }

        /// <summary>
        /// Creates a record.
        /// </summary>
        /// <param name="objectName">The name of the object in Salesforce.</param>
        /// <param name="record">The record to be created.</param>
        /// <returns>The Id of created record.</returns>
        public string Create (string objectName, object record) 
        {
            ExceptionHelper.ThrowIfNullOrEmpty("objectName", objectName);
            ExceptionHelper.ThrowIfNull("record", record);

            var response = Request<object>(GetUrl("sobjects"), objectName, record, Method.POST);
            return m_deserializer.Deserialize<dynamic>(response).id.Value;
        }

        /// <summary>
        /// Updates a record.
        /// </summary>
        /// <param name="objectName">The name of the object in Salesforce.</param>
        /// <param name="recordId">The record id.</param>
        /// <param name="record">The record to be updated.</param>
        public bool Update(string objectName, string recordId, object record)
        {
            ExceptionHelper.ThrowIfNullOrEmpty("objectName", objectName);
            ExceptionHelper.ThrowIfNullOrEmpty("recordId", recordId);
            ExceptionHelper.ThrowIfNull("record", record);

            var response = Request<object>(GetUrl("sobjects"), "{0}/{1}".With(objectName, recordId), record, Method.PATCH);

            // HTTP status code 204 is returned if an existing record is updated.
            var recordUpdated = response.StatusCode == HttpStatusCode.NoContent;                   
            
            return recordUpdated;
        }

        /// <summary>
        /// Deletes a record.
        /// </summary>
        /// <param name="objectName">The name of the object in Salesforce.</param>
        /// <param name="recordId">The record id which will be deleted.</param>
        /// <returns>True if was deleted, otherwise false.</returns>
        public bool Delete(string objectName, string recordId)
        {
            ExceptionHelper.ThrowIfNullOrEmpty("objectName", objectName);
            ExceptionHelper.ThrowIfNullOrEmpty("recordId", recordId);

            var response = Request<object>(GetUrl("sobjects"), "{0}/{1}".With(objectName, recordId), null, Method.DELETE);

            // HTTP status code 204 is returned if an existing record is deleted.
            var recoredDeleted = response.StatusCode == HttpStatusCode.NoContent;

            return recoredDeleted;
        }
        #endregion

        #region Requests
        /// <summary>
        /// Perform the request against Salesforce's REST API.
        /// </summary>
        /// <typeparam name="T">The return type.</typeparam>
        /// <param name="baseUrl">The base URL.</param>
        /// <param name="objectName">The Name of the object.</param>
        /// <param name="record">The record.</param>
        /// <param name="method">The http method.</param>
        /// <exception cref="System.InvalidOperationException">Please, execute Authenticate method before call any REST API operation.</exception>
        private IRestResponse<T> Request<T>(string baseUrl, string objectName = null, object record = null, Method method = Method.GET) where T : new()
        {
            if (!IsAuthenticated)
            {
                throw new InvalidOperationException("Please, execute Authenticate method before call any REST API operation.");
            }

            m_restClient.BaseUrl = baseUrl;
            var request = new RestRequest(objectName);
            request.RequestFormat = DataFormat.Json;
            request.Method = method;
            request.AddHeader("Authorization", "Bearer " + m_accessToken);

            if (record != null)
            {
                request.AddBody(record);
            }

            var response = m_restClient.Execute<T>(request);
            CheckApiException(response);

            return response;
        }

        /// <summary>
        /// Checks if an API exception was throw in the response.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <exception cref="SalesforceException">
        /// </exception>
        private void CheckApiException(IRestResponse response)
        {
            if ((int)response.StatusCode > 299)
            {
                var responseData = m_deserializer.Deserialize<dynamic>(response);

                var error = responseData[0];
                var fieldsArray = error.fields as JArray;

                if (fieldsArray == null)
                {
                    throw new SalesforceException(error.errorCode.Value, error.message.Value);
                }
                else
                {
                    throw new SalesforceException(error.errorCode.Value, error.message.Value, fieldsArray.Select(v => (string)v).ToArray());
                }
            }

            if (response.ErrorException != null)
            {
                throw response.ErrorException;
            }
        }
        #endregion

        #region Helpers
        /// <summary>
        /// Gets the record projection fields.
        /// </summary>
        /// <param name="recordType">Type of the record.</param>
        /// <returns></returns>
        private static string GetRecordProjection(Type recordType)
        {
            return String.Join(", ", recordType.GetProperties().Select(p => p.Name));
        }

        /// <summary>
        /// Gets the URL.
        /// </summary>
        /// <param name="resourceName">Name of the resource.</param>
        /// <returns></returns>
        private string GetUrl(string resourceName)
        {
            return "{0}/services/data/{1}/{2}".With(InstanceUrl, ApiVersion, resourceName);
        }
        #endregion
    }
}