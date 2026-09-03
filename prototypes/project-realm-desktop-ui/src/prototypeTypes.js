/**
 * @template T
 * @typedef {Object} KnowledgeField
 * @property {T} value
 * @property {string} source
 * @property {string} observedAt
 * @property {"较新"|"尚可"|"过期"|"未知"} freshness
 * @property {string} confidence
 */

/**
 * @typedef {Object} ActionAssessment
 * @property {"direct"|"request"|"unavailable"} mode
 * @property {string} modeLabel
 * @property {string} reason
 * @property {string} cost
 * @property {number} durationDays
 * @property {string} expected
 */

/**
 * @typedef {Object} PendingAction
 * @property {string} actionId
 * @property {string} targetId
 * @property {string} executorId
 * @property {number} submittedOnDay
 * @property {number} completesOnDay
 * @property {number} currentDay
 * @property {"waiting"|"resolved"} status
 */

/**
 * @typedef {Object} ActionResult
 * @property {"accepted"|"refused"|"delayed"|"partial"} status
 * @property {string} title
 * @property {string} summary
 * @property {string[]} effects
 * @property {string[]} causalChain
 * @property {string[]} eventIds
 */

/**
 * @typedef {Object} TimelineEvent
 * @property {string} id
 * @property {string} date
 * @property {string} category
 * @property {"normal"|"attention"|"important"|"major"} severity
 * @property {string} title
 * @property {string} detail
 */

export const ACTION_MODES = Object.freeze({
  DIRECT: "direct",
  REQUEST: "request",
  UNAVAILABLE: "unavailable",
});

export const EVENT_SEVERITIES = Object.freeze({
  NORMAL: "normal",
  ATTENTION: "attention",
  IMPORTANT: "important",
  MAJOR: "major",
});
