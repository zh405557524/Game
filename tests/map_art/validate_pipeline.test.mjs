import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { validateCatalogData } from '../../tools/map_art/validate_pipeline.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const catalogPath = path.resolve(here, '../../docs/00_项目总览/02_地图表现交付流程/pipeline-catalog.json');
const load = () => JSON.parse(fs.readFileSync(catalogPath, 'utf8'));
const errorsFor = catalog => validateCatalogData(catalog, catalogPath);

test('current five-stage catalog is valid', () => {
  assert.deepEqual(errorsFor(load()), []);
});

test('rejects skipping directly to material generation', () => {
  const catalog = load();
  const topic = catalog.topics.find(item => item.id === 'terrain.plain');
  topic.stages.materialGeneration = {
    status: 'WaitingReview', version: 'CandidateV1', path: '../../03_美术风格/03_美术资源生产/01_地形/README.md',
    inputVersion: null, approvalRecord: null, technicalStatus: 'Passed'
  };
  assert(errorsFor(catalog).some(error => error.includes('cannot start before unityPlan is Approved')));
});

test('rejects a downstream version built from a stale upstream version', () => {
  const catalog = load();
  const topic = catalog.topics.find(item => item.id === 'terrain.mountain');
  topic.stages.unityPlan = {
    status: 'WaitingReview', version: 'UnityPlanV1', path: '../../04_Unity实现/02_地图表现方案/01_地形/README.md',
    inputVersion: 'DesignGuidedV1', approvalRecord: null, technicalStatus: 'Passed'
  };
  assert(errorsFor(catalog).some(error => error.includes('does not match conceptDraft OriginalReferenceV1')));
});

test('technical success cannot replace a human approval record', () => {
  const catalog = load();
  const topic = catalog.topics.find(item => item.id === 'terrain.mountain');
  topic.stages.unityPlan = {
    status: 'Approved', version: 'UnityPlanV1', path: '../../04_Unity实现/02_地图表现方案/01_地形/README.md',
    inputVersion: 'OriginalReferenceV1', approvalRecord: null, technicalStatus: 'Passed'
  };
  assert(errorsFor(catalog).some(error => error.includes('Approved requires human approvalRecord')));
});
