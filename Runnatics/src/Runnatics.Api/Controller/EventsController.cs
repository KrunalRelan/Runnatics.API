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
            response.TotalCount = result.TotalCount;

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
        /// </remarks>
        /// <response code="204">Event created successfully with no content returned</response>
        /// <response code="400">If the request is invalid, contains invalid data, or validation fails</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="409">If an event with the same name and date already exists</response>
        /// <response code="500">If an internal server error occurs during creation</response>
        [HttpPost("create")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
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

            await _eventService.Create(request);

            if (_eventService.HasError)
            {
                // Return 409 Conflict for duplicate events
                if (_eventService.ErrorMessage.Contains("already exists"))
                {
                    return Conflict(_eventService.ErrorMessage);
                }

                // Return 400 Bad Request for validation errors
                if (_eventService.ErrorMessage.Contains("cannot be in the past") ||
                    _eventService.ErrorMessage.Contains("cannot be null"))
                {
                    return BadRequest(_eventService.ErrorMessage);
                }
                // Return 500 for database errors or unexpected errors
                return StatusCode((int)HttpStatusCode.InternalServerError, _eventService.ErrorMessage);
            }

            return Ok(HttpStatusCode.Created);
        }

        /// <summary>
        /// Deletes an event (soft delete)
        /// </summary>
        /// <param name="id">The ID of the event to delete</param>
        /// <returns>Success response if event was deleted</returns>
        /// <remarks>
        /// Sample request:
        /// 
        /// DELETE /api/events/5
        /// 
        /// **Authentication:**
        /// - Requires valid JWT token in Authorization header
        /// - Organization ID is automatically extracted from the token
        /// - User can only delete events from their own organization
        /// 
        /// **Behavior:**
        /// - Performs a soft delete (sets IsDeleted flag to true)
        /// - Event data is retained in the database
        /// - Deleted events will not appear in search results
        /// - Only events belonging to the user's organization can be deleted
        /// - Cannot delete events that don't exist
        /// 
        /// **Validation:**
        /// - Verifies event exists
        /// - Ensures event belongs to user's organization
        /// - Prevents deletion of already deleted events
        /// </remarks>
        /// <response code="200">Event deleted successfully</response>
        /// <response code="400">If the event ID is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized (Admin role required)</response>
        /// <response code="404">If the event is not found or doesn't belong to user's organization</response>
        /// <response code="500">If an internal server error occurs during deletion</response>
        [HttpDelete("{id}/delete-event")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(typeof(ResponseBase<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest(new { error = "Invalid event ID. ID must be a valid string." });
            }

            var response = new ResponseBase<object>();
            var result = await _eventService.Delete(id);

            if (_eventService.HasError)
            {
                response.Error = new ResponseBase<object>.ErrorData()
                {
                    Message = _eventService.ErrorMessage
                };

                // Return 404 Not Found if event doesn't exist or unauthorized
                if (_eventService.ErrorMessage.Contains("not found") ||
                       _eventService.ErrorMessage.Contains("does not exist") ||
                  _eventService.ErrorMessage.Contains("don't have permission"))
                {
                    return NotFound(response);
                }

                // Return 500 for database errors or unexpected errors
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            if (!result)
            {
                response.Error = new ResponseBase<object>.ErrorData()
                {
                    Message = "Event deletion failed. Please try again."
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = new { message = "Event deleted successfully", id };
            return Ok(response);
        }

        /// <summary>
        /// Updates an existing event
        /// </summary>
        /// <param name="id">The ID of the event to update</param>
        /// <param name="request">Updated event details including name, date, venue, settings, and configuration</param>
        /// <returns>The updated event with all associated settings</returns>
        /// <remarks>
        /// Sample request:
        /// 
        /// PUT /api/events/5
        ///   {
        ///         "name": "Mumbai Marathon 2024 - Updated",
        ///         "slug": "mumbai-marathon-2024",
        ///         "description": "Annual marathon event featuring multiple race categories - Updated description",
        ///         "eventDate": "2024-12-15T06:00:00Z",
        ///         "timeZone": "Asia/Kolkata",
        ///         "venueName": "Gateway of India",
        ///         "venueAddress": "Apollo Bandar, Colaba, Mumbai, Maharashtra 400001",
        ///         "venueLatitude": 18.9220,
        ///         "venueLongitude": 72.8347,
        ///         "status": "Active",
        ///         "maxParticipants": 6000,
        ///         "registrationDeadline": "2024-12-01T23:59:59Z",
        ///         "eventSettings": {
        ///             "removeBanner": false,
        ///             "published": true,
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
        ///       }
        ///   }
        /// 
        /// </remarks>
        /// <response code="200">Event updated successfully with all details</response>
        /// <response code="400">If the request is invalid, contains invalid data, or validation fails</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="404">If the event is not found or doesn't belong to user's organization</response>
        /// <response code="409">If an event with the same name and date already exists (when name/date changed)</response>
        /// <response code="500">If an internal server error occurs during update</response>
        [HttpPut("{id}/edit-event")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Update(string id, [FromBody] EventRequest request)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest(new { error = "Invalid event ID. ID must be a valid string." });
            }

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

            var result = await _eventService.Update(id, request);

            if (_eventService.HasError)
            {
                var response = new ResponseBase<object>
                {
                    Error = new ResponseBase<object>.ErrorData()
                    {
                        Message = _eventService.ErrorMessage
                    }
                };

                // Return 404 Not Found if event doesn't exist or unauthorized
                if (_eventService.ErrorMessage.Contains("not found") ||
                        _eventService.ErrorMessage.Contains("does not exist") ||
                        _eventService.ErrorMessage.Contains("don't have permission"))
                {
                    return NotFound(response);
                }

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

            if (!result)
            {
                var response = new ResponseBase<object>
                {
                    Error = new ResponseBase<object>.ErrorData()
                    {
                        Message = "Event update failed. Please try again."
                    }
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            return Ok(HttpStatusCode.OK);
        }

        [HttpGet("{eventId}/event-details")]
        public async Task<IActionResult> GetEvent(string eventId)
        {
            if (string.IsNullOrEmpty(eventId))
            {
                return BadRequest(new { error = "Invalid event ID. ID must be a valid string." });
            }

            var response = new ResponseBase<EventResponse>();
            var result = await _eventService.GetEventById(eventId);

            if (_eventService.HasError)
            {
                response.Error = new ResponseBase<EventResponse>.ErrorData()
                {
                    Message = _eventService.ErrorMessage
                };

                // Return 404 Not Found if event doesn't exist
                if (_eventService.ErrorMessage.Contains("not found") ||
                    _eventService.ErrorMessage.Contains("does not exist"))
                {
                    return NotFound(response);
                }

                // Return 500 for database errors or unexpected errors
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            if (result == null)
            {
                response.Error = new ResponseBase<EventResponse>.ErrorData()
                {
                    Message = "Event retrieval failed. Please try again."
                };
                return StatusCode((int)HttpStatusCode.InternalServerError, response);
            }

            response.Message = result;
            return Ok(response);
        }
    }
}