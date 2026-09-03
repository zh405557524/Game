#!/usr/bin/env node
// Read-only validation for the county-map five-stage art workflow.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const EXPECTED_STAGE_ORDER = [
  'designScheme',
  'conceptDraft',
  'unityPlan',
  'materialGeneration',
  'mapScene'
];

const EXPECTED_GROUP_COUNT = 12;
const EXPECTED_TOPIC_COUNT = 64;
const STARTED_STATUSES = new Set(['InProgress', 'WaitingReview', 'NeedsRevision', 'Approved']);
const COVERAGE_BLOCKS_DOWNSTREAM = new Set(['NeedsSpecification', 'Partial']);
const TECHNICAL_STATUSES = new Set(['NotRun', 'NotApplicable', 'Passed', 'Failed', 'Partial']);

function sameArray(left, right) {
  return left.length === right.length && left.every((value, index) => value === right[index]);
}

function exists(root, relativePath) {
  return typeof relativePath === 'string' && relativePath.length > 0 && fs.existsSync(path.resolve(root, relativePath));
}

export function validateCatalogData(catalog, catalogPath) {
  const errors = [];
  const root = path.dirname(path.resolve(catalogPath));
  const fail = message => errors.push(message);

  if (catalog?.schemaVersion !== 2) fail(`schemaVersion must be 2, got ${catalog?.schemaVersion}`);
  if (!Array.isArray(catalog?.allowedStatuses)) fail('allowedStatuses must be an array');
  if (!sameArray(catalog?.stageOrder ?? [], EXPECTED_STAGE_ORDER)) {
    fail(`stageOrder must be ${EXPECTED_STAGE_ORDER.join(' -> ')}`);
  }
  if (!Array.isArray(catalog?.groups) || catalog.groups.length !== EXPECTED_GROUP_COUNT) {
    fail(`groups must contain ${EXPECTED_GROUP_COUNT} entries`);
  }
  if (!Array.isArray(catalog?.topics) || catalog.topics.length !== EXPECTED_TOPIC_COUNT) {
    fail(`topics must contain ${EXPECTED_TOPIC_COUNT} entries`);
  }
  if (catalog?.primaryTopicCount !== EXPECTED_TOPIC_COUNT) {
    fail(`primaryTopicCount must be ${EXPECTED_TOPIC_COUNT}`);
  }

  const allowedStatuses = new Set(catalog?.allowedStatuses ?? []);
  const groupById = new Map();
  let declaredTopicCount = 0;
  for (const group of catalog?.groups ?? []) {
    if (!group?.id || groupById.has(group.id)) fail(`duplicate or missing group id: ${group?.id ?? '<missing>'}`);
    else groupById.set(group.id, group);
    declaredTopicCount += Number(group?.topicCount ?? 0);
    for (const field of ['designScheme', 'conceptDraft', 'unityPlan', 'materialGeneration', 'mapScene', 'scheme']) {
      if (!exists(root, group?.[field])) fail(`group ${group?.id}: missing ${field} path ${group?.[field] ?? '<null>'}`);
    }
    if (group?.referenceArchive && !exists(root, group.referenceArchive)) {
      fail(`group ${group.id}: missing referenceArchive ${group.referenceArchive}`);
    }
  }
  if (declaredTopicCount !== EXPECTED_TOPIC_COUNT) fail(`group topicCount sum must be ${EXPECTED_TOPIC_COUNT}`);

  const topicIds = new Set();
  const actualGroupCounts = new Map();
  for (const topic of catalog?.topics ?? []) {
    if (!topic?.id || topicIds.has(topic.id)) fail(`duplicate or missing topic id: ${topic?.id ?? '<missing>'}`);
    else topicIds.add(topic.id);
    if (!groupById.has(topic?.group)) fail(`topic ${topic?.id}: unknown group ${topic?.group}`);
    actualGroupCounts.set(topic?.group, (actualGroupCounts.get(topic?.group) ?? 0) + 1);

    for (const reference of topic?.referenceImages ?? []) {
      if (!exists(root, reference)) fail(`topic ${topic.id}: missing reference image ${reference}`);
    }

    const stages = topic?.stages ?? {};
    for (let index = 0; index < EXPECTED_STAGE_ORDER.length; index++) {
      const stageName = EXPECTED_STAGE_ORDER[index];
      const stage = stages[stageName];
      if (!stage) {
        fail(`topic ${topic.id}: missing stage ${stageName}`);
        continue;
      }
      if (!allowedStatuses.has(stage.status)) fail(`topic ${topic.id}/${stageName}: invalid status ${stage.status}`);
      if (!TECHNICAL_STATUSES.has(stage.technicalStatus)) {
        fail(`topic ${topic.id}/${stageName}: invalid technicalStatus ${stage.technicalStatus}`);
      }
      if (stage.status === 'NotStarted') {
        for (const field of ['version', 'path', 'inputVersion', 'approvalRecord']) {
          if (stage[field] !== null) fail(`topic ${topic.id}/${stageName}: NotStarted requires ${field}=null`);
        }
      }
      if (stage.path && !exists(root, stage.path)) fail(`topic ${topic.id}/${stageName}: missing path ${stage.path}`);
      if (stage.approvalRecord && !exists(root, stage.approvalRecord)) {
        fail(`topic ${topic.id}/${stageName}: missing approvalRecord ${stage.approvalRecord}`);
      }
      if (stage.status === 'Approved') {
        if (!stage.version) fail(`topic ${topic.id}/${stageName}: Approved requires version`);
        if (!stage.path) fail(`topic ${topic.id}/${stageName}: Approved requires path`);
        if (!stage.approvalRecord) fail(`topic ${topic.id}/${stageName}: Approved requires human approvalRecord`);
      }
      if (index > 0 && STARTED_STATUSES.has(stage.status)) {
        const previousName = EXPECTED_STAGE_ORDER[index - 1];
        const previous = stages[previousName];
        if (previous?.status !== 'Approved') {
          fail(`topic ${topic.id}/${stageName}: cannot start before ${previousName} is Approved`);
        } else if (stage.inputVersion !== previous.version) {
          fail(`topic ${topic.id}/${stageName}: inputVersion ${stage.inputVersion} does not match ${previousName} ${previous.version}`);
        }
      }
    }

    const coverage = stages.designScheme?.coverage;
    if (COVERAGE_BLOCKS_DOWNSTREAM.has(coverage)) {
      for (const stageName of EXPECTED_STAGE_ORDER.slice(1)) {
        const status = stages[stageName]?.status;
        if (status !== 'NotStarted' && status !== 'Archived') {
          fail(`topic ${topic.id}/${stageName}: ${coverage} design coverage blocks downstream work`);
        }
      }
    }

    if (topic?.legacy?.sourceCatalog && !exists(root, topic.legacy.sourceCatalog)) {
      fail(`topic ${topic.id}: missing legacy sourceCatalog ${topic.legacy.sourceCatalog}`);
    }
    if (topic?.legacy?.unityReport && !exists(root, topic.legacy.unityReport)) {
      fail(`topic ${topic.id}: missing legacy unityReport ${topic.legacy.unityReport}`);
    }
    for (const artifact of topic?.legacy?.artifacts ?? []) {
      if (artifact.exists === false) {
        if (!artifact.finding) fail(`topic ${topic.id}: absent legacy artifact requires a finding: ${artifact.path}`);
      } else if (!exists(root, artifact.path)) {
        fail(`topic ${topic.id}: missing legacy artifact ${artifact.path}`);
      }
    }
  }

  for (const [groupId, group] of groupById) {
    if ((actualGroupCounts.get(groupId) ?? 0) !== group.topicCount) {
      fail(`group ${groupId}: expected ${group.topicCount} topics, found ${actualGroupCounts.get(groupId) ?? 0}`);
    }
  }

  for (const source of catalog?.sourceDocuments ?? []) {
    if (!exists(root, source)) fail(`missing source document ${source}`);
  }
  if (catalog?.globalStyle?.status !== 'Approved') fail('globalStyle must remain explicitly Approved');
  if (!exists(root, catalog?.globalStyle?.path)) fail(`missing globalStyle path ${catalog?.globalStyle?.path}`);
  if (!exists(root, catalog?.globalStyle?.approvalRecord)) {
    fail(`missing globalStyle approvalRecord ${catalog?.globalStyle?.approvalRecord}`);
  }

  return errors;
}

export function validateCatalogFile(catalogPath) {
  const catalog = JSON.parse(fs.readFileSync(catalogPath, 'utf8'));
  return validateCatalogData(catalog, catalogPath);
}

const currentFile = fileURLToPath(import.meta.url);
if (process.argv[1] && path.resolve(process.argv[1]) === path.resolve(currentFile)) {
  const defaultCatalog = path.resolve(path.dirname(currentFile), '../../docs/00_项目总览/02_地图表现交付流程/pipeline-catalog.json');
  const catalogPath = path.resolve(process.argv[2] ?? defaultCatalog);
  try {
    const errors = validateCatalogFile(catalogPath);
    if (errors.length) {
      console.error(`Map art pipeline validation failed (${errors.length}):`);
      for (const error of errors) console.error(`- ${error}`);
      process.exitCode = 1;
    } else {
      const catalog = JSON.parse(fs.readFileSync(catalogPath, 'utf8'));
      console.log(`Map art pipeline OK: ${catalog.groups.length} groups, ${catalog.topics.length} topics, 5 ordered stages.`);
    }
  } catch (error) {
    console.error(error.stack || error.message);
    process.exitCode = 1;
  }
}
