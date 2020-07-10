﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using static RockLib.Messaging.HttpUtils;

namespace RockLib.Messaging.CloudEvents
{
    /// <summary>
    /// Defines a cloud event.
    /// </summary>
    public class CloudEvent
    {
        /// <summary>The name of the <see cref="Id"/> attribute.</summary>
        public const string IdAttribute = "id";

        /// <summary>The name of the <see cref="Source"/> attribute.</summary>
        public const string SourceAttribute = "source";

        /// <summary>The name of the <see cref="SpecVersion"/> attribute.</summary>
        public const string SpecVersionAttribute = "specversion";

        /// <summary>The name of the <see cref="Type"/> attribute.</summary>
        public const string TypeAttribute = "type";

        /// <summary>The name of the <see cref="DataContentType"/> attribute.</summary>
        public const string DataContentTypeAttribute = "datacontenttype";

        /// <summary>The name of the <see cref="DataSchema"/> attribute.</summary>
        public const string DataSchemaAttribute = "dataschema";

        /// <summary>The name of the <see cref="Subject"/> attribute.</summary>
        public const string SubjectAttribute = "subject";

        /// <summary>The name of the <see cref="Time"/> attribute.</summary>
        public const string TimeAttribute = "time";

        private const string _specVersion1_0 = "1.0";

        private static IProtocolBinding _defaultProtocolBinding;

        private string _id;
        private DateTime? _time;
        private object _data;

        /// <summary>
        /// Initializes a new instance of the <see cref="CloudEvent"/> class.
        /// </summary>
        public CloudEvent() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="CloudEvent"/> class based on the source
        /// cloud event. All cloud event attributes except <see cref="Id"/> and <see cref="Time"/>
        /// are copied to the new instance. Note that neither the source's <see cref="Data"/> nor
        /// any of its <see cref="AdditionalAttributes"/> are copied to the new instance.
        /// </summary>
        /// <param name="source">
        /// The source for cloud event attribute values.
        /// </param>
        public CloudEvent(CloudEvent source)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));

            Source = source.Source;
            Type = source.Type;
            DataContentType = source.DataContentType;
            DataSchema = source.DataSchema;
            Subject = source.Subject;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CloudEvent"/> class and sets its properties
        /// according to the payload and headers of the <paramref name="receiverMessage"/>.
        /// </summary>
        /// <param name="receiverMessage">
        /// The <see cref="IReceiverMessage"/> with headers that map to cloud event attributes.
        /// </param>
        /// <param name="protocolBinding">
        /// The <see cref="IProtocolBinding"/> used to map <see cref="IReceiverMessage"/> headers to
        /// CloudEvent attributes. If <see langword="null"/>, then <see cref="DefaultProtocolBinding"/>
        /// is used instead (and replaces the value of the <c>ref</c> parameter).
        /// </param>
        public CloudEvent(IReceiverMessage receiverMessage, IProtocolBinding protocolBinding = null)
        {
            if (receiverMessage is null)
                throw new ArgumentNullException(nameof(receiverMessage));

            if (protocolBinding is null)
                protocolBinding = DefaultProtocolBinding;

            foreach (var header in receiverMessage.Headers)
                AdditionalAttributes.Add(header);

            var specVersionHeader = protocolBinding.GetHeaderName(SpecVersionAttribute);
            if (receiverMessage.Headers.TryGetValue(specVersionHeader, out string specVersion))
            {
                if (specVersion != _specVersion1_0)
                    throw new CloudEventValidationException(
                        $"Invalid value found in '{specVersionHeader}' header. Expected '{_specVersion1_0}', but was '{specVersion}'.");
                AdditionalAttributes.Remove(specVersionHeader);
            }

            if (receiverMessage.IsBinary())
                _data = receiverMessage.BinaryPayload;
            else
                _data = receiverMessage.StringPayload;

            var idHeader = protocolBinding.GetHeaderName(IdAttribute);
            if (receiverMessage.Headers.TryGetValue(idHeader, out string id))
            {
                Id = id;
                AdditionalAttributes.Remove(idHeader);
            }

            var sourceHeader = protocolBinding.GetHeaderName(SourceAttribute);
            if (receiverMessage.Headers.TryGetValue(sourceHeader, out Uri source))
            {
                Source = source;
                AdditionalAttributes.Remove(sourceHeader);
            }

            var typeHeader = protocolBinding.GetHeaderName(TypeAttribute);
            if (receiverMessage.Headers.TryGetValue(typeHeader, out string type))
            {
                Type = type;
                AdditionalAttributes.Remove(typeHeader);
            }

            var dataContentHeader = protocolBinding.GetHeaderName(DataContentTypeAttribute);
            if (receiverMessage.Headers.TryGetValue(dataContentHeader, out ContentType dataContentType))
            {
                DataContentType = dataContentType;
                AdditionalAttributes.Remove(dataContentHeader);
            }
            else if (receiverMessage.Headers.TryGetValue(dataContentHeader, out string dataContentTypeString))
            {
                DataContentType = new ContentType(dataContentTypeString);
                AdditionalAttributes.Remove(dataContentHeader);
            }

            var dataSchemaHeader = protocolBinding.GetHeaderName(DataSchemaAttribute);
            if (receiverMessage.Headers.TryGetValue(dataSchemaHeader, out Uri dataSchema))
            {
                DataSchema = dataSchema;
                AdditionalAttributes.Remove(dataSchemaHeader);
            }

            var subjectHeader = protocolBinding.GetHeaderName(SubjectAttribute);
            if (receiverMessage.Headers.TryGetValue(subjectHeader, out string subject))
            {
                Subject = subject;
                AdditionalAttributes.Remove(subjectHeader);
            }

            var timeHeader = protocolBinding.GetHeaderName(TimeAttribute);
            if (receiverMessage.Headers.TryGetValue(timeHeader, out DateTime time))
            {
                Time = time;
                AdditionalAttributes.Remove(timeHeader);
            }
        }

        /// <summary>
        /// Gets or sets the default <see cref="IProtocolBinding"/>. This is used when one a
        /// <see cref="IProtocolBinding"/> is required by a cloud event method but was not provided
        /// (i.e. passed as <see langword="null"/>) by the caller.
        /// </summary>
        public static IProtocolBinding DefaultProtocolBinding
        {
            get => _defaultProtocolBinding ?? (_defaultProtocolBinding = ProtocolBinding.Default);
            set => _defaultProtocolBinding = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// REQUIRED. Identifies the event. Producers MUST ensure that source + id is unique for each
        /// distinct event. If a duplicate event is re-sent (e.g. due to a network error) it MAY have
        /// the same id. Consumers MAY assume that Events with identical source and id are duplicates.
        /// </summary>
        public string Id
        {
            get => _id ?? (_id = NewId());
            set
            {
                if (string.IsNullOrEmpty(value))
                    throw new ArgumentNullException(nameof(value));
                _id = value;
            }
        }

        /// <summary>
        /// REQUIRED. Identifies the context in which an event happened. Often this will include
        /// information such as the type of the event source, the organization publishing the event
        /// or the process that produced the event. The exact syntax and semantics behind the data
        /// encoded in the URI is defined by the event producer.
        /// </summary>
        public Uri Source { get; set; }

        /// <summary>
        /// The version of the CloudEvents specification which the event uses. This enables
        /// the interpretation of the context. Compliant event producers MUST use a value of '1.0'
        /// when referring to this version of the specification.
        /// </summary>
        public string SpecVersion => _specVersion1_0;

        /// <summary>
        /// REQUIRED. This attribute contains a value describing the type of event related to the
        /// originating occurrence. Often this attribute is used for routing, observability, policy
        /// enforcement, etc. The format of this is producer defined and might include information
        /// such as the version of the type.
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Content type of data value.
        /// </summary>
        public ContentType DataContentType { get; set; }

        /// <summary>
        /// Identifies the schema that data adheres to. Incompatible changes to the schema SHOULD be
        /// reflected by a different URI.
        /// </summary>
        public Uri DataSchema { get; set; }

        /// <summary>
        /// This describes the subject of the event in the context of the event producer (identified
        /// by source). In publish-subscribe scenarios, a subscriber will typically subscribe to events
        /// emitted by a source, but the source identifier alone might not be sufficient as a qualifier
        /// for any specific event if the source context has internal sub-structure.
        /// 
        /// <para>Identifying the subject of the event in context metadata (opposed to only in the data
        /// payload) is particularly helpful in generic subscription filtering scenarios where middleware
        /// is unable to interpret the data content. In the above example, the subscriber might only be
        /// interested in blobs with names ending with '.jpg' or '.jpeg' and the subject attribute allows
        /// for constructing a simple and efficient string-suffix filter for that subset of events.</para>
        /// </summary>
        public string Subject { get; set; }

        /// <summary>
        /// Timestamp of when the occurrence happened.
        /// </summary>
        public DateTime Time
        {
            get => _time ?? (_time = CurrentTime()).Value;
            set => _time = value;
        }

        /// <summary>
        /// Domain-specific information about the occurrence (i.e. the payload). This might include
        /// information about the occurrence, details about the data that was changed, or more.
        /// <para>
        /// When setting this property, the value must be a string, byte array, or <see langword=
        /// "null"/>.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentException">
        /// When setting this property, if the value is not a string, byte array, or <see langword=
        /// "null"/>.
        /// </exception>
        public object Data
        {
            get => _data;
            set
            {
                switch (value)
                {
                    case string _:
                    case byte[] _:
                    case null:
                        _data = value;
                        break;
                    default:
                        throw new ArgumentException("Data property must be a string, byte array, or null.", nameof(value));
                }
            }
        }

        /// <summary>
        /// Any additional attributes not specific to this CloudEvent type.
        /// </summary>
        public IDictionary<string, object> AdditionalAttributes { get; } = new Dictionary<string, object>();

        /// <summary>
        /// Creates a <see cref="SenderMessage"/> with headers mapped from the attributes of this cloud event.
        /// </summary>
        /// <param name="protocolBinding">
        /// The <see cref="IProtocolBinding"/> used to map CloudEvent attributes to <see cref="SenderMessage"/>
        /// headers. If <see langword="null"/>, then <see cref="DefaultProtocolBinding"/> is used instead.
        /// </param>
        /// <returns>The mapped <see cref="SenderMessage"/>.</returns>
        /// <exception cref="CloudEventValidationException">If the cloud event is invalid.</exception>
        public virtual SenderMessage ToSenderMessage(IProtocolBinding protocolBinding = null)
        {
            Validate();

            if (protocolBinding is null)
                protocolBinding = DefaultProtocolBinding;

            SenderMessage senderMessage;

            if (_data is string stringData)
                senderMessage = new SenderMessage(stringData);
            else if (_data is byte[] binaryData)
                senderMessage = new SenderMessage(binaryData);
            else
                senderMessage = new SenderMessage("");

            senderMessage.Headers[protocolBinding.GetHeaderName(IdAttribute)] = Id;
            senderMessage.Headers[protocolBinding.GetHeaderName(SourceAttribute)] = Source;
            senderMessage.Headers[protocolBinding.GetHeaderName(SpecVersionAttribute)] = SpecVersion;
            senderMessage.Headers[protocolBinding.GetHeaderName(TypeAttribute)] = Type;

            if (DataContentType != null)
                senderMessage.Headers[protocolBinding.GetHeaderName(DataContentTypeAttribute)] = DataContentType;

            if (DataSchema != null)
                senderMessage.Headers[protocolBinding.GetHeaderName(DataSchemaAttribute)] = DataSchema;

            if (Subject != null)
                senderMessage.Headers[protocolBinding.GetHeaderName(SubjectAttribute)] = Subject;

            senderMessage.Headers[protocolBinding.GetHeaderName(TimeAttribute)] = Time;

            foreach (var attribute in AdditionalAttributes)
                senderMessage.Headers[attribute.Key] = attribute.Value;

            return senderMessage;
        }

        /// <summary>
        /// Creates an <see cref="HttpRequestMessage"/> with headers mapped from the attributes of this cloud event.
        /// </summary>
        /// <param name="requestUri">A string that represents the request <see cref="Uri"/>.</param>
        /// <param name="protocolBinding">
        /// The <see cref="IProtocolBinding"/> used to map CloudEvent attributes to <see cref="HttpRequestMessage"/>
        /// headers. If <see langword="null"/>, then <see cref="DefaultProtocolBinding"/> is used instead.
        /// </param>
        /// <returns>The mapped <see cref="HttpRequestMessage"/>.</returns>
        public HttpRequestMessage ToHttpRequestMessage(string requestUri = null, IProtocolBinding protocolBinding = null) =>
            ToHttpRequestMessage(HttpMethod.Get, requestUri, protocolBinding);

        /// <summary>
        /// Creates an <see cref="HttpRequestMessage"/> with headers mapped from the attributes of this cloud event.
        /// </summary>
        /// <param name="method">The HTTP method of the request.</param>
        /// <param name="requestUri">A string that represents the request <see cref="Uri"/>.</param>
        /// <param name="protocolBinding">
        /// The <see cref="IProtocolBinding"/> used to map CloudEvent attributes to <see cref="HttpRequestMessage"/>
        /// headers. If <see langword="null"/>, then <see cref="DefaultProtocolBinding"/> is used instead.
        /// </param>
        /// <returns>The mapped <see cref="HttpRequestMessage"/>.</returns>
        public HttpRequestMessage ToHttpRequestMessage(HttpMethod method, string requestUri = null, IProtocolBinding protocolBinding = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            var message = ToSenderMessage(protocolBinding);
            var request = new HttpRequestMessage(method, requestUri);

            if (message.IsBinary)
                request.Content = new ByteArrayContent(message.BinaryPayload);
            else
                request.Content = new StringContent(message.StringPayload);

            if (DataContentType != null)
            {
                request.Content.Headers.ContentType =
                    new MediaTypeHeaderValue(DataContentType.MediaType)
                    {
                        CharSet = DataContentType.CharSet
                    };
            }

            foreach (var header in message.Headers)
            {
                var headers = IsContentHeader(header.Key)
                    ? request.Content.Headers
                    : (HttpHeaders)request.Headers;

                AddHeader(headers, header.Key, header.Value?.ToString());
            }

            return request;
        }

        /// <summary>
        /// Converts the <see cref="CloudEvent"/> to a <see cref="SenderMessage"/>.
        /// </summary>
        /// <param name="cloudEvent">The <see cref="CloudEvent"/> to convert to a <see cref="SenderMessage"/>.</param>
        public static implicit operator SenderMessage(CloudEvent cloudEvent) =>
            cloudEvent?.ToSenderMessage(DefaultProtocolBinding);

        /// <summary>
        /// Ensures that the cloud event is valid - throws a <see cref="CloudEventValidationException"/>
        /// if it is not. May also set missing property values that can be determined at runtime.
        /// </summary>
        /// <exception cref="CloudEventValidationException">If the cloud event is invalid.</exception>
        public virtual void Validate()
        {
            if (Source is null)
                throw new CloudEventValidationException("Source cannot be null.");

            if (string.IsNullOrEmpty(Type))
                throw new CloudEventValidationException("Type cannot be null or empty.");
        }

        /// <summary>
        /// Ensures that the required base cloud event attributes are present.
        /// </summary>
        /// <param name="senderMessage">The <see cref="SenderMessage"/> to validate.</param>
        /// <param name="protocolBinding">
        /// The <see cref="IProtocolBinding"/> used to map CloudEvent attributes to <see cref="SenderMessage"/>
        /// headers. If <see langword="null"/>, then <see cref="DefaultProtocolBinding"/> is used instead
        /// (and replaces the value of the <c>ref</c> parameter).
        /// </param>
        protected internal static void ValidateCore(SenderMessage senderMessage, IProtocolBinding protocolBinding)
        {
            if (senderMessage is null)
                throw new ArgumentNullException(nameof(senderMessage));

            if (protocolBinding is null)
                protocolBinding = DefaultProtocolBinding;

            var specVersionHeader = protocolBinding.GetHeaderName(SpecVersionAttribute);
            if (!TryGetHeaderValue<string>(senderMessage, specVersionHeader, out var specVersion))
                throw new CloudEventValidationException($"The '{specVersionHeader}' header is missing from the SenderMessage.");
            else if (specVersion != _specVersion1_0)
                throw new CloudEventValidationException($"The '{specVersionHeader}' header must have a value of '{_specVersion1_0}'.");

            var idHeader = protocolBinding.GetHeaderName(IdAttribute);
            if (!ContainsHeader<string>(senderMessage, idHeader))
                senderMessage.Headers[idHeader] = NewId();

            var sourceHeader = protocolBinding.GetHeaderName(SourceAttribute);
            if (!ContainsHeader<Uri>(senderMessage, sourceHeader))
                throw new CloudEventValidationException($"The '{sourceHeader}' header is missing from the SenderMessage.");

            var typeHeader = protocolBinding.GetHeaderName(TypeAttribute);
            if (!ContainsHeader<string>(senderMessage, typeHeader))
                throw new CloudEventValidationException($"The '{typeHeader}' header is missing from the SenderMessage.");

            var timeHeader = protocolBinding.GetHeaderName(TimeAttribute);
            if (!ContainsHeader<DateTime>(senderMessage, timeHeader))
                senderMessage.Headers[timeHeader] = CurrentTime();
        }

        /// <summary>
        /// Returns whether the <paramref name="senderMessage"/> has a header with a name matching
        /// the <paramref name="headerName"/> and a value of either type <typeparamref name="T"/>
        /// or a type convertible to <typeparamref name="T"/>.
        /// </summary>
        /// <param name="senderMessage">The sender message to check</param>
        /// <param name="headerName">The name of the header.</param>
        /// <typeparam name="T">The type of the header value.</typeparam>
        /// <returns>
        /// <see langword="true"/> if the the <paramref name="senderMessage"/> has a header with a
        /// name matching the <paramref name="headerName"/> and a value of either type
        /// <typeparamref name="T"/> or a type convertible to <typeparamref name="T"/>; otherwise,
        /// <see langword="false"/>.
        /// </returns>
        protected static bool ContainsHeader<T>(SenderMessage senderMessage, string headerName)
        {
            if (senderMessage.Headers.TryGetValue(headerName, out var objectValue))
            {
                switch (objectValue)
                {
                    case T _:
                        return true;
                    case null:
                        return false;
                }

                var converter = TypeDescriptor.GetConverter(typeof(T));
                if (converter.CanConvertFrom(objectValue.GetType()))
                    try
                    {
                        converter.ConvertFrom(objectValue);
                        return true;
                    }
                    catch { }

                converter = TypeDescriptor.GetConverter(objectValue);
                if (converter.CanConvertTo(typeof(T)))
                    try
                    {
                        converter.ConvertTo(objectValue, typeof(T));
                        return true;
                    }
                    catch { }
            }

            return false;
        }

        /// <summary>
        /// Gets the value of the header with the specified name as type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type of the header value.</typeparam>
        /// <param name="senderMessage">The <see cref="SenderMessage"/>.</param>
        /// <param name="headerName">The name of the header.</param>
        /// <param name="value">
        /// When this method returns, the value of the header with the specified name, if the
        /// header is found; otherwise, the default value for the type of the <paramref name="
        /// value"/> parameter. This parameter is passed uninitialized.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the the <paramref name="senderMessage"/> has a header with a
        /// name matching the <paramref name="headerName"/> and a value of either type
        /// <typeparamref name="T"/> or a type convertible to <typeparamref name="T"/>; otherwise,
        /// <see langword="false"/>.
        /// </returns>
        protected static bool TryGetHeaderValue<T>(SenderMessage senderMessage, string headerName, out T value)
        {
            if (senderMessage.Headers.TryGetValue(headerName, out var objectValue))
            {
                switch (objectValue)
                {
                    case T variable:
                        value = variable;
                        return true;
                    case null:
                        value = default;
                        return false;
                }

                if (typeof(T) == typeof(DateTime) && objectValue is string stringValue)
                {
                    if (DateTime.TryParse(stringValue, null, DateTimeStyles.RoundtripKind, out var dateTimeValue))
                    {
                        value = (T)(object)dateTimeValue;
                        return true;
                    }
                }

                var converter = TypeDescriptor.GetConverter(typeof(T));
                if (converter.CanConvertFrom(objectValue.GetType()))
                {
                    try
                    {
                        value = (T)converter.ConvertFrom(objectValue);
                        return true;
                    }
                    catch
                    {
                    }
                }

                converter = TypeDescriptor.GetConverter(objectValue);
                if (converter.CanConvertTo(typeof(T)))
                {
                    try
                    {
                        value = (T)converter.ConvertTo(objectValue, typeof(T));
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            value = default;
            return false;
        }

        private static string NewId() => Guid.NewGuid().ToString();

        private static DateTime CurrentTime() => DateTime.UtcNow;
    }
}
