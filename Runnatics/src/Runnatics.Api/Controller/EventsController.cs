using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Runnatics.Models.Client.Common;
using Runnatics.Models.Client.Requests.Events;
using Runnatics.Models.Client.Responses.Events;
using Runnatics.Services.Interface;
using System.Net;

namespace Runnatics.Api.Controller
{
    /// <summary>
    /// Controller for managing race events including creation, search, and event settings management
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class EventsController(IEventsService eventService) : ControllerBase
    {
        private readonly IEventsService _eventService = eventService;

        /// <summary>
        /// Searches for events based on specified criteria with pagination and sorting
        /// </summary>
        /// <param name="request">Search criteria including filters, pagination, and sorting options</param>
        /// <returns>A paginated list of events matching the search criteria</returns>
        /// <remarks>
        /// Sample request:
        /// 
        /// POST /api/events/search
        ///     {
        ///       "name": "Marathon",
        ///       "status": "Active",
        ///       "eventDateFrom": "2024-01-01T00:00:00Z",
        ///       "eventDateTo": "2024-12-31T23:59:59Z",
        ///       "pageNumber": 1,
        ///       "pageSize": 10,
        ///       "sortFieldName": "EventDate",
        ///       "sortDirection": 0
        ///     }
        ///     
        /// **Authentication:**
        /// - Requires valid JWT token in Authorization header
        /// - Organization ID is automatically extracted from the token
        /// - User can only search events from their own organization
        /// 
        /// **Optional Filters:**
        /// - **id**: Specific event ID for exact match (optional)
        /// - **name**: Partial match on event name (optional)
        /// - **status**: Event status - Draft, Active, InProgress, Completed, Cancelled (optional)
        /// - **eventDateFrom**: Start date range filter (optional)
        /// - **eventDateTo**: End date range filter (optional)
        /// - **pageNumber**: Page number for pagination (default: 1)
        /// - **pageSize**: Number of items per page (default: 100, max: 1000)
        /// - **sortFieldName**: Field name to sort by (e.g., "Name", "EventDate", "Status") (default: "EventDate")
        /// - **sortDirection**: 0 for Ascending, 1 for Descending (default: Descending)
        /// 
        /// **Behavior:**
        /// - Returns ALL active events for user's organization when no filters are provided
        /// - Apply additional filters to narrow down results
        /// - Only returns active (non-deleted) events
        /// - Results are paginated and sorted as specified
        /// - Multi-tenant isolation enforced through JWT token
        /// </remarks>
        /// <response code="200">Returns the paginated list of events with total count</response>
        /// <response code="400">If the request is invalid or contains invalid data</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized (Admin role required)</response>
        /// <response code="500">If an internal server error occurs during search</response>
        [HttpPost("search")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<PagingList<EventResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Search([FromBody] EventSearchRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { error = "Invalid input provided. Request body cannot be null." });
            }

            // Validate model state
            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    error = "Validation failed",
                    details = ModelState.Values
                          .SelectMany(v => v.Errors)
                       .Select(e => e.ErrorMessage)
                         .ToList()
                });
            }

            var response = new ResponseBase<PagingList<EventResponse>>();
            var result = await _eventService.Search(request);

            if (_eventService.HasError)
            {
                response.Error = new ResponseBase<PagingList<EventResponse>>.ErrorData()
                {
                    Message = _eventService.ErrorMessage
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            response.Message.TotalCount = result.TotalCount;

            return Ok(response);
        }

        /// <summary>
        /// Creates a new event
        /// </summary>
        /// <param name="request">Event details including name, date, venue, settings, and configuration</param>
        /// <returns>The newly created event with all associated settings</returns>
        /// <remarks>
        /// Sample request:
        /// 
        /// POST /api/events/create
        ///   {
        ///         "name": "Mumbai Marathon 2024",
        ///         "slug": "mumbai-marathon-2024",
        ///         "description": "Annual marathon event featuring multiple race categories",
        ///         "eventDate": "2024-12-15T06:00:00Z",
        ///         "timeZone": "Asia/Kolkata",
        ///         "venueName": "Gateway of India",
        ///         "venueAddress": "Apollo Bandar, Colaba, Mumbai, Maharashtra 400001",
        ///         "venueLatitude": 18.9220,
        ///         "venueLongitude": 72.8347,
        ///         "status": "Draft",
        ///         "maxParticipants": 5000,
        ///         "registrationDeadline": "2024-12-01T23:59:59Z",
        ///         "eventSettings": {
        ///             "removeBanner": false,
        ///             "published": false,
        ///             "rankOnNet": true,
        ///             "showResultSummaryForRaces": true,
        ///             "useOldData": false,
        ///             "confirmedEvent": true,
        ///             "allowNameCheck": true,
        ///             "allowParticipantEdit": true
        ///         },
        ///         "leaderboardSettings": {
        ///             "showOverallResults": true,
        ///             "showCategoryResults": true,
        ///             "showGenderResults": true,
        ///             "showAgeGroupResults": true,
        ///             "enableLiveLeaderboard": true,
        ///             "showSplitTimes": true,
        ///             "showPace": true,
        ///             "showTeamResults": false,
        ///             "showMedalIcon": true,
        ///             "allowAnonymousView": true,
        ///             "autoRefreshIntervalSec": 30,
        ///             "maxDisplayedRecords": 100
        ///         },
        ///     }
        /// 
        /// **Authentication:**
        /// - Requires valid JWT token in Authorization header
        /// - **organizationId**: Automatically extracted from JWT token (not in request body)
        /// - **createdBy**: Automatically extracted from JWT token (not in request body)
        /// - User can only create events for their own organization
        /// - Multi-tenant isolation enforced
        ///     
        /// **Required Fields:**
        /// - **name**: Event name (max 255 characters)
        /// - **slug**: URL-friendly identifier (max 100 characters)
        /// - **eventDate**: Date and time of the event (must be in the future)
        /// 
        /// **Optional Fields:**
        /// - **description**: Detailed event description
        /// - **timeZone**: Event timezone (default: "Asia/Kolkata")
        /// - **venueName**: Name of the venue (max 255 characters)
        /// - **venueAddress**: Full address of the venue
        /// - **venueLatitude**: GPS latitude coordinate
        /// - **venueLongitude**: GPS longitude coordinate
        /// - **status**: Event status (default: "Draft") - Draft, Active, InProgress, Completed, Cancelled
        /// - **maxParticipants**: Maximum number of participants allowed
        /// - **registrationDeadline**: Last date for participant registration
        /// - **eventSettings**: Event-specific configuration settings
        /// - **leaderboardSettings**: Leaderboard display and behavior settings
        /// 
        /// **Event Settings:**
        /// - All boolean flags with sensible defaults
        /// - Controls event visibility, editing permissions, and display options
        /// 
        /// **Leaderboard Settings:**
        /// - **autoRefreshIntervalSec**: Refresh interval (5-300 seconds, default: 30)
        /// - **maxDisplayedRecords**: Records to display (10-1000, default: 100)
        /// - Controls result display options and real-time updates
        /// 
        /// **Validation:**
        /// - Prevents creating events with past dates
        /// - Checks for duplicate events (same name, date, organization)
        /// - Validates all required fields and data formats
        /// - Ensures organization ID from token matches event organization
        /// 
        /// **Response:**
        /// Returns the complete event object including:
        /// - Generated event ID
        /// - All event details
        /// - Associated event settings (if provided)
        /// - Associated leaderboard settings (if provided)
        /// - Organization information
        /// - Audit information (created date, created by)
        /// </remarks>
        /// <response code="201">Event created successfully with all details</response>
        /// <response code="400">If the request is invalid, contains invalid data, or validation fails</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="409">If an event with the same name and date already exists</response>
        /// <response code="500">If an internal server error occurs during creation</response>
        [HttpPost("create")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<EventResponse>), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Create([FromBody] EventRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { error = "Invalid input provided. Request body cannot be null." });
            }

            // Validate model state
            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    error = "Validation failed",
                    details = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList()
                });
            }

            var response = new ResponseBase<EventResponse>();
            var result = await _eventService.Create(request);

            if (_eventService.HasError)
            {
                response.Error = new ResponseBase<EventResponse>.ErrorData()
                {
                    Message = _eventService.ErrorMessage
                };

                // Return 409 Conflict for duplicate events
                if (_eventService.ErrorMessage.Contains("already exists"))
                {
                    return Conflict(response);
                }

                // Return 400 Bad Request for validation errors
                if (_eventService.ErrorMessage.Contains("cannot be in the past") ||
                    _eventService.ErrorMessage.Contains("cannot be null"))
                {
                    return BadRequest(response);
                }

                // Return 500 for database errors or unexpected errors
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            if (result == null)
            {
                response.Error = new ResponseBase<EventResponse>.ErrorData()
                {
                    Message = "Event creation failed. Please try again."
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return StatusCode((int)HttpStatusCode.Created, response);
        }
    }
}
