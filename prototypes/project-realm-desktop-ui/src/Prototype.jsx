import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  ArrowRight,
  Bell,
  BookOpenText,
  Buildings,
  CalendarDots,
  CaretRight,
  CheckCircle,
  Clock,
  CloudRain,
  Coins,
  Compass,
  Drop,
  Eye,
  FastForward,
  Gear,
  Grains,
  Handshake,
  House,
  Info,
  ListDashes,
  LockKey,
  MagnifyingGlass,
  MapPin,
  MapTrifold,
  Pause,
  PersonSimpleRun,
  Plant,
  Play,
  Question,
  Scales,
  Seal,
  Sun,
  Toolbox,
  User,
  UsersThree,
  Warning,
  X,
} from "@phosphor-icons/react";
import snapshot from "./data/xiao-county-snapshot.json";
import world from "./data/xiao-county-world.json";

const SPEEDS = [1, 2, 4];
const ZOOM_LEVELS = [
  { label: "县", scale: 1 },
  { label: "乡／镇", scale: 1.12 },
  { label: "村", scale: 1.28 },
];

const DATE_LABELS = ["四月初三", "四月初四", "四月初五", "四月初六", "四月初七", "四月初八", "四月初九"];
const RESOURCE_LABELS = {
  agriculture: "农业",
  forest: "林木",
  pasture: "牧业",
  fishery: "渔业",
  salt: "盐业",
  fuel: "燃料",
  metal: "金属",
  buildingMaterial: "营造",
};
const ALIVE_STATUS_LABELS = {
  alive_confirmed: "在世可证",
  alive_probable: "或仍在世",
  deceased_legacy: "前代人物",
};
const ADMIN_ROLE_LABELS = {
  county_school_teacher: "县学教习",
};
const TRAVEL_METADATA = new Map(snapshot.mapNodes.map((node) => [node.id, node]));

function normalizeMapCoordinate(value, minimum, maximum) {
  return 7 + ((value - minimum) / Math.max(1, maximum - minimum)) * 86;
}

const DIVISIONS = world.divisions.map((division) => ({
  ...division,
  entityType: "division",
  kind: division.type,
  x: normalizeMapCoordinate(division.relativeX, world.coordinateBounds.minX, world.coordinateBounds.maxX),
  y: normalizeMapCoordinate(division.relativeY, world.coordinateBounds.minY, world.coordinateBounds.maxY),
  population: `约 ${division.residentPopulation.toLocaleString("zh-CN")} 口 · ${division.settlementCount} 处聚落`,
}));
const DIVISION_BY_ID = new Map(DIVISIONS.map((division) => [division.id, division]));

const SETTLEMENTS = world.settlements.map((settlement) => {
  const travel = TRAVEL_METADATA.get(settlement.id) ?? {};
  return {
    ...settlement,
    ...travel,
    entityType: "settlement",
    kind: settlement.type,
    x: normalizeMapCoordinate(settlement.relativeX, world.coordinateBounds.minX, world.coordinateBounds.maxX),
    y: normalizeMapCoordinate(settlement.relativeY, world.coordinateBounds.minY, world.coordinateBounds.maxY),
    population: `约 ${settlement.residentPopulation.toLocaleString("zh-CN")} 口`,
    minimumZoom: 2,
  };
});
const SETTLEMENT_BY_ID = new Map(SETTLEMENTS.map((settlement) => [settlement.id, settlement]));
const KNOWN_DESTINATIONS = SETTLEMENTS.filter((settlement) => TRAVEL_METADATA.get(settlement.id)?.arrival);
const KNOWN_DESTINATION_IDS = new Set(KNOWN_DESTINATIONS.map((settlement) => settlement.id));
const FOCUS_VILLAGE = SETTLEMENT_BY_ID.get(snapshot.focusVillage.id);
const COUNTY_SEAT = SETTLEMENT_BY_ID.get(snapshot.countySeatPlan.id);

function dateLabel(day) {
  return DATE_LABELS[day] ?? `四月第 ${day + 3} 日`;
}

function assessTravel(origin, destination) {
  if (origin.id === destination.id) return { days: 0, route: "当前所在", cost: "—" };
  if (destination.id === snapshot.player.initialLocationId) {
    const days = Math.max(1, origin.travelDays ?? 1);
    return { days, route: "来时旧路", cost: `随身干粮${days === 1 ? "一" : "二"}日` };
  }
  return {
    days: destination.travelDays ?? 1,
    route: destination.route ?? `经${destination.divisionName}乡道`,
    cost: destination.travelCost ?? "随身干粮一日",
  };
}

function IconButton({ label, active = false, disabled = false, onClick, children, className = "" }) {
  return (
    <button
      type="button"
      className={`icon-button ${active ? "is-active" : ""} ${className}`}
      aria-label={label}
      title={label}
      disabled={disabled}
      onClick={onClick}
    >
      {children}
    </button>
  );
}

function PanelTabs({ tabs, value, onChange, ariaLabel }) {
  return (
    <div className="panel-tabs" role="tablist" aria-label={ariaLabel}>
      {tabs.map((tab) => (
        <button
          type="button"
          role="tab"
          aria-selected={value === tab.value}
          className={value === tab.value ? "is-active" : ""}
          key={tab.value}
          onClick={() => onChange(tab.value)}
        >
          {tab.label}
        </button>
      ))}
    </div>
  );
}

function KnowledgeRow({ icon: Icon, label, field, revealed = true }) {
  const isUnknown = !revealed || field.freshness === "未知";
  return (
    <div className={`knowledge-row ${isUnknown ? "is-unknown" : ""}`}>
      <div className="knowledge-icon" aria-hidden="true"><Icon size={18} weight="regular" /></div>
      <div className="knowledge-copy">
        <div className="knowledge-main">
          <span>{label}</span>
          <strong>{revealed ? field.value : "尚未查明"}</strong>
        </div>
        <div className="knowledge-meta">
          <span>{revealed ? field.source : "需要亲见或询问知情人"}</span>
          <span>{revealed ? `${field.observedAt} · ${field.confidence}` : "未知"}</span>
        </div>
      </div>
    </div>
  );
}

function SeverityMark({ severity, children }) {
  const labels = {
    normal: "常",
    attention: "察",
    important: "要",
    major: "急",
  };
  return <span className={`severity severity-${severity}`}>{children ?? labels[severity]}</span>;
}

function MapNodeIcon({ kind }) {
  if (kind === "county_seat" || kind === "town") return <Buildings size={18} weight="fill" />;
  if (kind === "township") return <MapTrifold size={17} weight="fill" />;
  if (kind === "market_town") return <Coins size={17} weight="fill" />;
  if (kind === "bridge") return <MapPin size={17} weight="fill" />;
  if (kind === "ferry") return <Drop size={17} weight="fill" />;
  if (kind === "market") return <Coins size={17} weight="fill" />;
  return <House size={17} weight="fill" />;
}

function CountyPlaceIcon({ type }) {
  const icons = {
    county_yamen: Scales,
    official_school: BookOpenText,
    market: Coins,
    clinic_pharmacy: Info,
    temple_monastery: House,
    workshop: Toolbox,
    military_compound: LockKey,
    dock: Drop,
  };
  const Icon = icons[type] ?? Buildings;
  return <Icon size={19} weight="regular" />;
}

function MapViewport({ zoomLevel, setZoomLevel, selectedNodeId, selectedDivisionId, onSelectNode, onSelectDivision, villageStatus, playerOpen, travelOpen, playerLocation, onOpenPlayer }) {
  const [offset, setOffset] = useState({ x: 0, y: 0 });
  const [viewportSize, setViewportSize] = useState({ width: 760, height: 600 });
  const viewportRef = useRef(null);
  const drag = useRef(null);
  const selectedDivision = DIVISION_BY_ID.get(selectedDivisionId) ?? DIVISION_BY_ID.get(snapshot.focusVillage.divisionId);
  const visibleDivisions = zoomLevel === 0
    ? DIVISIONS.filter((division) => division.type === "town" || division.id === selectedDivisionId)
    : zoomLevel === 1
      ? DIVISIONS
      : [];
  const visibleSettlements = zoomLevel === 2
    ? SETTLEMENTS.filter((settlement) => settlement.divisionId === selectedDivisionId)
    : [];
  const visibleVillageCount = visibleSettlements.filter((settlement) => settlement.type === "village").length;
  const settlementScopeLabel = visibleSettlements.length > 0 && visibleVillageCount === visibleSettlements.length
    ? `${visibleVillageCount}个村落`
    : `${selectedDivision.settlementCount}处聚落`;
  const settlementXs = visibleSettlements.map((settlement) => settlement.x);
  const settlementYs = visibleSettlements.map((settlement) => settlement.y);
  const focusX = zoomLevel === 2 && settlementXs.length
    ? (Math.min(...settlementXs) + Math.max(...settlementXs)) / 2
    : 50;
  const focusY = zoomLevel === 2 && settlementYs.length
    ? (Math.min(...settlementYs) + Math.max(...settlementYs)) / 2
    : 50;
  const divisionSpan = zoomLevel === 2 && settlementXs.length
    ? Math.max(Math.max(...settlementXs) - Math.min(...settlementXs), Math.max(...settlementYs) - Math.min(...settlementYs))
    : 100;
  const scale = zoomLevel === 2
    ? Math.max(3.1, Math.min(6.2, 58 / Math.max(1, divisionSpan)))
    : ZOOM_LEVELS[zoomLevel].scale;
  const semanticOffset = {
    x: viewportSize.width * (0.5 - (scale * focusX) / 100),
    y: viewportSize.height * (0.5 - (scale * focusY) / 100),
  };

  useEffect(() => {
    if (!viewportRef.current) return undefined;
    const observer = new ResizeObserver(([entry]) => {
      setViewportSize({ width: entry.contentRect.width, height: entry.contentRect.height });
    });
    observer.observe(viewportRef.current);
    return () => observer.disconnect();
  }, []);

  const onPointerDown = (event) => {
    if (event.button !== 0 || event.target.closest("button")) return;
    event.currentTarget.setPointerCapture(event.pointerId);
    drag.current = { startX: event.clientX, startY: event.clientY, originX: offset.x, originY: offset.y };
  };

  const onPointerMove = (event) => {
    if (!drag.current) return;
    setOffset({
      x: Math.max(-130, Math.min(130, drag.current.originX + event.clientX - drag.current.startX)),
      y: Math.max(-90, Math.min(90, drag.current.originY + event.clientY - drag.current.startY)),
    });
  };

  const stopDrag = (event) => {
    drag.current = null;
    if (event.currentTarget.hasPointerCapture?.(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
  };

  const onWheel = (event) => {
    event.preventDefault();
    setZoomLevel((current) => Math.max(0, Math.min(2, current + (event.deltaY > 0 ? -1 : 1))));
  };

  return (
    <section
      className="map-viewport"
      ref={viewportRef}
      aria-label="萧县县域、乡镇计算区与聚落地图"
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={stopDrag}
      onPointerCancel={stopDrag}
      onWheel={onWheel}
    >
      <div className="map-fade map-fade-top" />
      <div
        className="map-transform"
        style={{
          "--marker-scale": 1 / scale,
          transform: `translate3d(${semanticOffset.x + offset.x}px, ${semanticOffset.y + offset.y}px, 0) scale(${scale})`,
        }}
      >
        <img src="/assets/xiao-county-ink-map.png" alt="萧县水墨地图，河流、农田与聚落散布其间" draggable="false" />
        {visibleDivisions.map((division) => {
          const compact = zoomLevel === 1 && division.type !== "town" && division.id !== selectedDivisionId;
          return (
            <button
              type="button"
              key={division.id}
              className={`map-node division-node node-${division.kind} ${selectedDivisionId === division.id ? "is-selected" : ""} ${compact ? "is-compact" : ""}`}
              style={{ left: `${division.x}%`, top: `${division.y}%` }}
              aria-label={`${division.name}，${division.typeLabel}，${division.population}`}
              onClick={(event) => {
                event.stopPropagation();
                onSelectDivision(division);
              }}
            >
              <span className="node-marker"><MapNodeIcon kind={division.kind} /></span>
              <span className="node-name">{division.name}</span>
              <span className="node-tooltip">{division.typeLabel} · {division.population}</span>
            </button>
          );
        })}
        {visibleSettlements.map((settlement) => {
          const labeled = settlement.id === selectedNodeId
            || settlement.id === playerLocation.id
            || settlement.id === selectedDivision.centerSettlementId;
          const featured = labeled || KNOWN_DESTINATION_IDS.has(settlement.id);
          return (
            <button
              type="button"
              key={settlement.id}
              className={`map-node settlement-node node-${settlement.kind} ${selectedNodeId === settlement.id ? "is-selected" : ""} ${featured ? "is-featured" : "is-compact"} ${labeled ? "is-labeled" : "is-name-hidden"} ${settlement.id === snapshot.focusVillage.id && villageStatus !== "blocked" ? `status-${villageStatus}` : ""}`}
              style={{ left: `${settlement.x}%`, top: `${settlement.y}%` }}
              aria-label={`${settlement.name}，${settlement.typeLabel}，${settlement.population}，隶属${settlement.divisionName}`}
              onClick={(event) => {
                event.stopPropagation();
                onSelectNode(settlement);
              }}
            >
              <span className="node-marker"><MapNodeIcon kind={settlement.kind} /></span>
              <span className="node-name">{settlement.name}</span>
              <span className="node-tooltip">{settlement.typeLabel} · {settlement.population}</span>
            </button>
          );
        })}
        <button
          type="button"
          className={`player-map-model ${playerOpen || travelOpen ? "is-active" : ""}`}
          style={{ left: `${Math.min(93, playerLocation.x + 6)}%`, top: `${Math.min(92, playerLocation.y + 3)}%` }}
          aria-label={`我的人物：${snapshot.player.name}，当前在${playerLocation.name}，点击查看个人属性`}
          aria-haspopup="dialog"
          aria-expanded={playerOpen}
          onClick={(event) => {
            event.stopPropagation();
            onOpenPlayer();
          }}
        >
          <span className="player-model-marker"><img src={snapshot.player.modelAsset} alt="" aria-hidden="true" /></span>
          <span className="player-model-label">我</span>
          <span className="player-model-tooltip">{snapshot.player.name} · 正在{playerLocation.name}</span>
        </button>
      </div>

      <div className="map-scope-card" aria-live="polite">
        <span>{zoomLevel === 2 ? "村落视图 · 最小世界单元" : `${ZOOM_LEVELS[zoomLevel].label}级视图`}</span>
        <strong>{zoomLevel < 2 ? `萧县 · ${world.validation.divisionCount}个乡镇计算区` : `${selectedDivision.name} · ${settlementScopeLabel}`}</strong>
        <small>{zoomLevel === 2 ? `${selectedDivision.subregionName} · 点击村落查看详情` : "上级行政地理尚未探索"}</small>
      </div>

      <div className="map-compass" aria-label="地图方向">
        <Compass size={20} weight="regular" />
        <span>北</span>
      </div>

      <div className="map-legend" aria-label="地图图例">
        {zoomLevel < 2 ? (
          <>
            <span><Buildings size={15} weight="fill" />镇级区</span>
            <span><MapTrifold size={15} weight="fill" />乡级区</span>
            <span><MapPin size={15} weight="fill" />计算中心</span>
          </>
        ) : (
          <>
            <span><Buildings size={15} weight="fill" />县城／镇市</span>
            <span><House size={15} weight="fill" />村落</span>
            <span><MapPin size={15} weight="fill" />区域中心</span>
          </>
        )}
        <span className="legend-drought"><Sun size={15} />少雨</span>
      </div>

      <div className="map-zoom" aria-label="语义缩放层级">
        {ZOOM_LEVELS.map((level, index) => (
          <button
            type="button"
            key={level.label}
            className={index === zoomLevel ? "is-active" : ""}
            onClick={() => setZoomLevel(index)}
          >
            {level.label}
          </button>
        ))}
      </div>
    </section>
  );
}

function DivisionOverview({ division, onEnter }) {
  const strongestResources = Object.entries(division.resources)
    .sort((left, right) => right[1] - left[1])
    .slice(0, 3);
  return (
    <div className="division-overview panel-scroll">
      <div className="division-role-strip">
        <MapTrifold size={23} weight="fill" />
        <span><strong>{division.typeLabel}</strong><small>{division.isCountyCore ? "县城所在计算区" : `${division.directionName} · ${division.subregionName}`}</small></span>
      </div>
      <div className="division-stat-grid">
        <div><span>推定人口</span><strong>{division.residentPopulation.toLocaleString("zh-CN")} 口</strong></div>
        <div><span>推定户数</span><strong>{division.householdCount.toLocaleString("zh-CN")} 户</strong></div>
        <div><span>聚落</span><strong>{division.settlementCount} 处</strong></div>
        <div><span>中心</span><strong>{division.centerSettlementName}</strong></div>
      </div>
      <section className="division-resource-card">
        <span className="eyebrow">主要资源指数</span>
        {strongestResources.map(([code, value]) => (
          <div className="division-resource-row" key={code}>
            <span>{RESOURCE_LABELS[code]}</span><div className="meter"><i style={{ width: `${value}%` }} /></div><strong>{value}</strong>
          </div>
        ))}
      </section>
      <div className="source-warning">
        <Warning size={17} weight="fill" />
        <span>这是确定性计算区，不是已经证实的明代乡界；名称与边界均不得包装为精确史实。</span>
      </div>
      <button type="button" className="primary-ink-button division-enter-button" onClick={() => onEnter(division)}>
        查看所属聚落<MapPin size={17} />
      </button>
    </div>
  );
}

function LeftPanel({ tab, setTab, selectedNode, currentLocationId, countyVisited, investigated, stage, onAsk, onOpenActions, onOpenSource, onOpenTravel, onCountyPlace, onOpenDivision, onExplainUnknownRoute }) {
  const isDivision = selectedNode.entityType === "division";
  const isFocus = selectedNode.id === snapshot.focusVillage.id;
  const isCountySeat = selectedNode.id === snapshot.countySeatPlan.id;
  const isCurrent = selectedNode.id === currentLocationId;
  const isKnownDestination = KNOWN_DESTINATION_IDS.has(selectedNode.id);
  const canalField = investigated
    ? {
        value: "主渠东段淤塞，西田断水",
        source: `${snapshot.person.name}口述；你已到渠首查看`,
        observedAt: "夏季 · 四月初三",
        freshness: "较新",
        confidence: "亲见",
      }
    : snapshot.knowledge.canal;

  return (
    <aside className="left-panel paper-panel" aria-label="选中地点信息">
      <header className="panel-heading location-heading">
        <div>
          <span className="eyebrow">所见之地</span>
          <h1>{selectedNode.name}</h1>
        </div>
        <IconButton label="查看数据来源" onClick={onOpenSource}><Info size={19} /></IconButton>
      </header>

      {isDivision ? (
        <DivisionOverview division={selectedNode} onEnter={onOpenDivision} />
      ) : isFocus ? (
        <>
          <div className="location-vignette">
            <img src="/assets/xiao-county-ink-map.png" alt="七里村周边河谷与农田" />
            <div className="vignette-caption">萧县 · 南江桥乡 · 七里村｜河谷村居，田依水脉。</div>
          </div>

          <PanelTabs
            ariaLabel="七里村信息分类"
            value={tab}
            onChange={setTab}
            tabs={[
              { value: "overview", label: "概览" },
              { value: "production", label: "生产" },
              { value: "people", label: "人物" },
              { value: "recent", label: "近事" },
            ]}
          />

          <div className="panel-scroll" role="tabpanel">
            {tab === "overview" && (
              <div className="knowledge-list">
                <KnowledgeRow icon={UsersThree} label="人口" field={snapshot.knowledge.population} />
                <KnowledgeRow icon={Plant} label="田地" field={snapshot.knowledge.farmland} />
                <KnowledgeRow icon={Drop} label="主渠" field={canalField} revealed={investigated} />
                <KnowledgeRow icon={Grains} label="公仓" field={snapshot.knowledge.granary} />
                <div className="source-boundary">
                  <Warning size={17} weight="fill" />
                  <span>你只看到自己接触过的地方。未知并不等于没有。</span>
                </div>
              </div>
            )}

            {tab === "production" && (
              <div className="production-view">
                <h3>春田用水</h3>
                <div className="meter-row"><span>东田</span><div className="meter"><i style={{ width: "72%" }} /></div><strong>尚可</strong></div>
                <div className="meter-row"><span>西田</span><div className="meter meter-danger"><i style={{ width: stage === "resolved" ? "46%" : "18%" }} /></div><strong>{stage === "resolved" ? "恢复中" : "断水"}</strong></div>
                <div className="production-note">
                  <Sun size={20} />
                  <p>少雨使支渠水势减弱；若主渠五日内仍未疏通，西田春苗将先受损。</p>
                </div>
                <button type="button" className="text-action" onClick={investigated ? onOpenActions : onAsk}>
                  {investigated ? "商议修渠" : "查问水情"}<CaretRight size={16} />
                </button>
              </div>
            )}

            {tab === "people" && (
              <div className="village-people-preview">
                <div className="compact-person">
                  <img src="/assets/li-zhengmao-portrait.png" alt="七里村生成角色小像" />
                  <div>
                    <span className="evidence-chip evidence-generated">生成居民</span>
                    <h3>{snapshot.person.name}</h3>
                    <p>{snapshot.person.position} · {snapshot.person.occupation}</p>
                    <p>你所知：{snapshot.person.publicReputation}，与你同村相识。</p>
                    <button type="button" className="text-action" onClick={onAsk}>向他询问<CaretRight size={16} /></button>
                  </div>
                </div>
                <div className="people-mini-list">
                  {snapshot.generatedPeople.slice(1, 4).map((person) => (
                    <div key={person.id}><User size={16} /><span><strong>{person.name}</strong><small>{person.occupation} · {person.publicStatus}</small></span></div>
                  ))}
                </div>
                <div className="source-boundary"><Info size={17} /><span>七里村本次展开 471 人、86 户；以上姓名均为确定性生成角色，historical_claim=no。</span></div>
              </div>
            )}

            {tab === "recent" && (
              <div className="recent-list">
                <div><SeverityMark severity="attention" /><p><strong>三月廿八</strong>　旬内少雨，河面比往常低。</p></div>
                <div><SeverityMark severity="major" /><p><strong>四月初三</strong>　西田今日未能引水。</p></div>
                {investigated && <div><SeverityMark severity="important" /><p><strong>四月初三</strong>　{snapshot.person.name}确认东渠淤塞。</p></div>}
                {stage === "resolved" && <div><SeverityMark severity="important" /><p><strong>四月初六</strong>　十二户允诺出工，主渠开清。</p></div>}
              </div>
            )}
          </div>
        </>
      ) : isCountySeat ? (
        <>
          <div className="location-vignette county-seat-vignette">
            <img src="/assets/xiao-county-seat-vignette.png" alt="萧县城南门、城墙、街市、县署与河埠的纸本水墨鸟瞰" />
            <div className="vignette-caption">县治聚人、货、文书与差役；你能进入城中，却不会因此获得县衙后台总账。</div>
          </div>

          <PanelTabs
            ariaLabel="萧县城信息分类"
            value={tab}
            onChange={setTab}
            tabs={[
              { value: "overview", label: "概览" },
              { value: "districts", label: "城内" },
              { value: "people", label: "人物" },
              { value: "recent", label: "近事" },
            ]}
          />

          <div className="panel-scroll county-seat-content" role="tabpanel">
            {tab === "overview" && (
              <div className="county-overview">
                <div className="county-role-strip"><Buildings size={22} weight="fill" /><span><strong>{snapshot.countySeatPlan.role} · {snapshot.countySeatPlan.divisionName}</strong><small>县内公开地理 · 上级行政地理仍未探索</small></span></div>
                <div className="knowledge-list">
                  <KnowledgeRow icon={UsersThree} label="聚居人口" field={{ value: snapshot.countySeatPlan.populationDisplay, source: snapshot.countySeatPlan.populationSource, observedAt: "静态快照", freshness: "推定", confidence: "约数" }} />
                  <KnowledgeRow icon={Scales} label="县署" field={{ value: "县署设于城内", source: "县治设施规划", observedAt: "抵达前可知", freshness: "尚可", confidence: "类型已知" }} />
                  <KnowledgeRow icon={Coins} label="市集" field={{ value: countyVisited ? "南门街市可交易" : "城中有市，行情待查", source: countyVisited ? "你已到城内亲见" : "过路人口述", observedAt: countyVisited ? "最近一次抵达" : "四月初二", freshness: countyVisited ? "较新" : "可能过期", confidence: countyVisited ? "亲见" : "口述" }} />
                  <KnowledgeRow icon={LockKey} label="县级总账" field={{ value: "普通村民无权查阅", source: "身份与权限边界", observedAt: "—", freshness: "未知", confidence: "不公开" }} />
                </div>
                <div className="source-boundary"><Eye size={17} /><span>{snapshot.countySeatPlan.visibilityNote}</span></div>
                {isCurrent ? (
                  <div className="current-location-note"><CheckCircle size={18} weight="fill" /><span>你已抵达萧县城 · 南门外，可查看城内地点</span></div>
                ) : (
                  <button type="button" className="primary-ink-button county-travel-button" onClick={() => onOpenTravel(selectedNode.id)}>
                    前往萧县城<PersonSimpleRun size={18} />
                  </button>
                )}
              </div>
            )}

            {tab === "districts" && (
              countyVisited ? (
                <div className="county-place-list">
                  {snapshot.countySeatPlan.places.map((place) => (
                    <button type="button" className="county-place-card" key={place.id} onClick={() => onCountyPlace(place)}>
                      <span className="county-place-icon"><CountyPlaceIcon type={place.id} /></span>
                      <span><strong>{place.name}</strong><small>{place.type} · {place.access}</small><em>{place.detail}</em></span>
                      <CaretRight size={15} />
                    </button>
                  ))}
                </div>
              ) : (
                <div className="county-unexplored">
                  <LockKey size={28} />
                  <h3>城内细节尚未亲见</h3>
                  <p>你知道县城有县署、市集和码头，但不会在抵达前获得完整街区情报。</p>
                  <button type="button" className="primary-ink-button" onClick={() => onOpenTravel(selectedNode.id)}>进城探索<PersonSimpleRun size={17} /></button>
                </div>
              )
            )}

            {tab === "people" && (
              countyVisited ? (
                <div className="county-people-list">
                  {snapshot.countySeatPlan.knownPeople.map((person) => (
                    <button type="button" key={person.id} onClick={() => onCountyPlace({ name: person.name, detail: person.known })}>
                      <User size={20} /><span><strong>{person.name} · {person.role}</strong><small>{person.where} · 生成角色</small><em>{person.known}</em></span><CaretRight size={15} />
                    </button>
                  ))}
                  <div className="hidden-mind-note"><Eye size={18} /><span>县城人多不等于全知；姓名、关系和真实态度仍要逐一接触。</span></div>
                </div>
              ) : (
                <div className="county-unexplored"><UsersThree size={28} /><h3>只听过职役，尚不识其人</h3><p>抵达县城后，才能接触门役、书铺代笔与药铺郎中。</p></div>
              )
            )}

            {tab === "recent" && (
              <div className="recent-list county-recent-list">
                {snapshot.countySeatPlan.recent.map((event) => (
                  <div key={event.title}><SeverityMark severity={event.severity} /><p><strong>{event.date}</strong>　{event.title}。{event.detail}</p></div>
                ))}
                {!countyVisited && <div className="county-hearsay-note"><Clock size={17} /><span>以上均为抵达前口述，可能已经变化。</span></div>}
              </div>
            )}
          </div>
        </>
      ) : (
        <div className="other-location">
          <MapPin size={28} />
          <h2>{selectedNode.name}</h2>
          <p>{selectedNode.typeLabel} · {selectedNode.population}</p>
          <div className="settlement-hierarchy-note"><MapTrifold size={17} /><span>{snapshot.metadata.county} → {selectedNode.divisionName} → {selectedNode.name}</span></div>
          <div className="stale-box">
            <Clock size={18} />
            <span>你只从过路人口中听过此地，近况可能已经变化。</span>
          </div>
          {selectedNode.type === "village" && (
            isCurrent ? (
              <div className="current-location-note"><CheckCircle size={18} weight="fill" /><span>你当前正在此村</span></div>
            ) : isKnownDestination ? (
              <button type="button" className="primary-ink-button location-travel-button" onClick={() => onOpenTravel(selectedNode.id)}>
                前往此村<PersonSimpleRun size={18} />
              </button>
            ) : (
              <button type="button" className="secondary-button location-travel-button" onClick={() => onExplainUnknownRoute(selectedNode)}>
                路线尚未掌握<Question size={18} />
              </button>
            )
          )}
          <button type="button" className="text-action" onClick={() => onOpenSource(selectedNode)}>查看已知来源<CaretRight size={16} /></button>
        </div>
      )}
    </aside>
  );
}

function AffairsPanel({ investigated, stage, pending, result, onAsk, onOpenActions, onShowResult, onOpenCounty, setToast }) {
  let primaryLabel = "查问渠情";
  let primaryAction = onAsk;
  if (investigated) {
    primaryLabel = "商议修渠";
    primaryAction = onOpenActions;
  }
  if (stage === "pending") {
    primaryLabel = `等候回报 · ${Math.max(0, pending.completesOnDay - pending.currentDay)} 日`;
    primaryAction = () => setToast("命令已送达，推进时间即可等待回应。", "info");
  }
  if (stage === "resolved") {
    primaryLabel = "查看修渠结果";
    primaryAction = onShowResult;
  }

  return (
    <div className="affairs-view">
      <section className="affair-card affair-primary">
        <div className="affair-title">
          <SeverityMark severity={stage === "resolved" ? "important" : "major"} />
          <div><span className="eyebrow">七里村 · 民生</span><h3>{stage === "resolved" ? "主渠开始清淤" : "西田断水"}</h3></div>
        </div>
        <p>{stage === "resolved" ? result?.summary : investigated ? "已确认东渠淤塞；各户尚未形成共同出工的约定。" : "主渠东段疑有淤塞，尚未查明具体位置和各户打算。"}</p>
        <button type="button" className="primary-ink-button" onClick={primaryAction}>
          {primaryLabel}<ArrowRight size={17} />
        </button>
      </section>

      <div className="affair-list" aria-label="其他待办">
        <button type="button" onClick={() => setToast("麦价观察属于后续栏目，本轮原型未开放。", "locked")}>
          <SeverityMark severity="attention" /><span><strong>麦价小涨</strong><small>昨日从平河桥传来</small></span><span>察</span>
        </button>
        <button type="button" onClick={onOpenCounty}>
          <SeverityMark severity="normal" /><span><strong>县城文书到村</strong><small>选中萧县城查看县治规划</small></span><span>往</span>
        </button>
      </div>

      <section className="related-person-card">
        <img src="/assets/li-zhengmao-portrait.png" alt="七里村生成角色小像" />
        <div>
          <span className="eyebrow">相关人物 · 生成居民</span>
          <h3>{snapshot.person.name}</h3>
          <p>{snapshot.person.position} · {snapshot.person.publicReputation}</p>
          <small>{stage === "resolved" ? "已答应先召集十二户开清主渠。" : snapshot.person.knownActivity}</small>
        </div>
      </section>
    </div>
  );
}

function PeoplePanel({ investigated, stage, onAsk, setToast }) {
  const [mode, setMode] = useState("residents");
  const [selectedResidentId, setSelectedResidentId] = useState(snapshot.person.id);
  const [selectedHistoricalId, setSelectedHistoricalId] = useState(world.historicalPeople[0]?.id);
  const selectedResident = snapshot.generatedPeople.find((person) => person.id === selectedResidentId) ?? snapshot.generatedPeople[0];
  const selectedHistorical = world.historicalPeople.find((person) => person.id === selectedHistoricalId) ?? world.historicalPeople[0];

  return (
    <div className="people-view">
      <div className="people-subtabs" role="tablist" aria-label="人物资料分类">
        {[
          { value: "residents", label: "乡里人物" },
          { value: "administration", label: "行政体系" },
          { value: "history", label: "人物志" },
        ].map((item) => (
          <button type="button" role="tab" aria-selected={mode === item.value} className={mode === item.value ? "is-active" : ""} key={item.value} onClick={() => setMode(item.value)}>{item.label}</button>
        ))}
      </div>

      {mode === "residents" && (
        <div className="people-mode-panel">
          <div className="resident-feature">
            <img className="person-portrait" src="/assets/li-zhengmao-portrait.png" alt="七里村生成角色小像" />
            <div className="person-heading">
              <span className="evidence-chip evidence-generated">确定性生成</span>
              <div><h2>{selectedResident.name}</h2><span>{selectedResident.age} 岁</span></div>
              <p>{selectedResident.registration} · {selectedResident.role}</p>
            </div>
          </div>
          <dl className="person-facts">
            <div><dt>主业</dt><dd>{selectedResident.occupation}</dd></div>
            <div><dt>处境</dt><dd>{selectedResident.socialStratum}</dd></div>
            <div><dt>公开身份</dt><dd>{selectedResident.publicStatus}</dd></div>
            <div><dt>史实声明</dt><dd>否；游戏生成居民</dd></div>
          </dl>
          <div className="resident-list" aria-label="七里村代表人物">
            {snapshot.generatedPeople.map((person) => (
              <button type="button" className={selectedResident.id === person.id ? "is-active" : ""} key={person.id} onClick={() => setSelectedResidentId(person.id)}>
                <User size={17} /><span><strong>{person.name}</strong><small>{person.occupation} · {person.role}</small></span>
              </button>
            ))}
          </div>
          <div className="hidden-mind-note"><Eye size={18} /><span>只展示可观察身份；忠诚、野心、怨恨等后台数值仍隐藏。</span></div>
          {selectedResident.id === snapshot.person.id && (
            <button type="button" className="primary-ink-button" onClick={onAsk}>{investigated ? "再问各户意向" : "询问渠情"}<ArrowRight size={17} /></button>
          )}
        </div>
      )}

      {mode === "administration" && (
        <div className="people-mode-panel administration-panel">
          <div className="administration-chain">
            {world.administration.levels.map((level, index) => (
              <div className="administration-level" key={level.id}>
                <span className="administration-index">{index + 1}</span>
                <div><span className="eyebrow">{level.label} · {level.nature}</span><strong>{level.name}</strong><p>{level.authority}</p></div>
              </div>
            ))}
          </div>
          <section className="role-quota-card">
            <span className="eyebrow">萧县职业结构投影</span>
            {world.administration.roleQuotaProjection.map((role) => (
              <button type="button" key={role.occupation_code} onClick={() => setToast(`${ADMIN_ROLE_LABELS[role.occupation_code] ?? role.occupation_name_zh_hans}：县域结构投影 ${role.worker_count_est} 人，不等于同时在任官缺。`, "info")}>
                <Scales size={16} /><span><strong>{ADMIN_ROLE_LABELS[role.occupation_code] ?? role.occupation_name_zh_hans}</strong><small>结构投影 {role.worker_count_est.toLocaleString("zh-CN")} 人</small></span><Info size={14} />
              </button>
            ))}
          </section>
          <div className="source-warning"><Warning size={18} /><span>{world.administration.warning}</span></div>
        </div>
      )}

      {mode === "history" && selectedHistorical && (
        <div className="people-mode-panel history-panel">
          <div className="historical-feature">
            <span className="historical-medallion"><BookOpenText size={25} weight="duotone" /></span>
            <div>
              <span className="evidence-chip evidence-history">来源人物 · {selectedHistorical.evidenceGrade}级</span>
              <h2>{selectedHistorical.name}</h2>
              <p>{ALIVE_STATUS_LABELS[selectedHistorical.aliveStatus] ?? "生卒状态不详"}{selectedHistorical.ageAtSnapshot ? ` · 时年约${selectedHistorical.ageAtSnapshot}岁` : ""}</p>
            </div>
          </div>
          <dl className="person-facts historical-facts">
            <div><dt>萧县关联</dt><dd>{selectedHistorical.associations.map((association) => association.name).join("；")}</dd></div>
            <div><dt>功名</dt><dd>{selectedHistorical.highestExamBeforeSnapshot || "未见记录"}</dd></div>
            <div><dt>最高官职</dt><dd>{selectedHistorical.highestOfficeBeforeSnapshot || "未见记录"}</dd></div>
            <div><dt>开局位置</dt><dd>{selectedHistorical.associations.some((association) => association.presentAtSnapshot) ? "有在县证据" : "不在县内生成可见NPC"}</dd></div>
          </dl>
          <div className="historical-list" aria-label="萧县关联历史人物">
            {world.historicalPeople.map((person) => (
              <button type="button" className={selectedHistorical.id === person.id ? "is-active" : ""} key={person.id} onClick={() => setSelectedHistoricalId(person.id)}>
                <BookOpenText size={16} /><span><strong>{person.name}</strong><small>{ALIVE_STATUS_LABELS[person.aliveStatus] ?? "状态不详"} · {person.associations[0]?.name}</small></span><em>{person.evidenceGrade}</em>
              </button>
            ))}
          </div>
          <div className="source-warning"><Warning size={18} /><span>县域关联不等于本人开局就在萧县；CBDB样本偏向精英，本原型仅作来源人物入口。</span></div>
        </div>
      )}
    </div>
  );
}

function RightPanel(props) {
  const { tab, setTab, setToast } = props;
  return (
    <aside className="right-panel paper-panel" aria-label="政事与人物">
      <PanelTabs
        ariaLabel="右侧信息分类"
        value={tab}
        onChange={setTab}
        tabs={[{ value: "affairs", label: "政事" }, { value: "people", label: "人物" }]}
      />
      <div className="right-scroll" role="tabpanel">
        {tab === "affairs" ? <AffairsPanel {...props} /> : <PeoplePanel {...props} />}
      </div>
      <nav className="bottom-nav" aria-label="主要栏目">
        {[
          { label: "天下", icon: MapTrifold },
          { label: "人物", icon: User },
          { label: "政事", icon: Scales },
          { label: "四季", icon: BookOpenText },
        ].map(({ label, icon: Icon }) => (
          <button type="button" key={label} onClick={() => setToast(`${label}栏目在本轮原型中未开放。`, "locked")}>
            <Icon size={23} weight="regular" /><span>{label}</span>
          </button>
        ))}
      </nav>
    </aside>
  );
}

function Chronicle({ events, stage, pending, day, paused, setPaused, advanceDays, onShowResult }) {
  const remaining = pending?.status === "waiting" ? Math.max(0, pending.completesOnDay - day) : 0;
  return (
    <section className="chronicle paper-panel" aria-label="县域纪事">
      <div className="chronicle-mark"><BookOpenText size={25} /><span>纪<br />事</span></div>
      <div className="chronicle-events" aria-live="polite">
        {events.slice(-4).map((event) => (
          <div className="chronicle-row" key={event.id}>
            <span className={`category category-${event.severity}`}>{event.category}</span>
            <time>{event.date}</time>
            <p>{event.title}，{event.detail}</p>
          </div>
        ))}
      </div>
      <div className="chronicle-scene" aria-hidden="true"><img src="/assets/xiao-county-ink-map.png" alt="" /></div>
      <div className="chronicle-control">
        {stage === "pending" ? (
          <button type="button" className="continue-button" onClick={() => advanceDays(remaining)}>
            <Clock size={23} /><span>等待回报<small>推进 {remaining} 日</small></span>
          </button>
        ) : stage === "resolved" ? (
          <button type="button" className="continue-button" onClick={onShowResult}>
            <CheckCircle size={23} /><span>查看结果<small>世界已暂停</small></span>
          </button>
        ) : (
          <button type="button" className="continue-button" onClick={() => setPaused(!paused)}>
            {paused ? <Play size={23} weight="fill" /> : <Pause size={23} weight="fill" />}
            <span>{paused ? "继续" : "暂停"}<small>{paused ? "空格" : "世界运行中"}</small></span>
          </button>
        )}
      </div>
    </section>
  );
}

function ActionSheet({ selectedActionId, setSelectedActionId, onClose, onSubmit }) {
  const selected = snapshot.actions.find((action) => action.id === selectedActionId);
  const modeIcons = { direct: PersonSimpleRun, request: Handshake, unavailable: LockKey };
  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section className="action-sheet paper-panel" role="dialog" aria-modal="true" aria-labelledby="action-title" onMouseDown={(event) => event.stopPropagation()}>
        <header className="modal-heading">
          <div><span className="eyebrow">七里村 · 行动评估</span><h2 id="action-title">如何处理主渠淤塞</h2></div>
          <IconButton label="关闭行动面板" onClick={onClose}><X size={21} /></IconButton>
        </header>
        <p className="modal-intro">同一意图会因身份、职位、财产与实际控制关系呈现不同方式。选择一种行动查看原因。</p>

        <div className="action-options" role="radiogroup" aria-label="修渠行动方式">
          {snapshot.actions.map((action) => {
            const ModeIcon = modeIcons[action.mode];
            return (
              <button
                type="button"
                role="radio"
                aria-checked={selectedActionId === action.id}
                className={`action-option mode-${action.mode} ${selectedActionId === action.id ? "is-selected" : ""}`}
                key={action.id}
                onClick={() => setSelectedActionId(action.id)}
              >
                <span className="action-mode-icon"><ModeIcon size={24} weight="regular" /></span>
                <span className="action-option-copy"><strong>{action.title}</strong><small>{action.modeLabel}</small></span>
                <CaretRight size={18} />
              </button>
            );
          })}
        </div>

        <div className={`assessment mode-${selected.mode}`}>
          <div className="assessment-badge">
            {selected.mode === "direct" && <PersonSimpleRun size={20} />}
            {selected.mode === "request" && <Handshake size={20} />}
            {selected.mode === "unavailable" && <LockKey size={20} />}
            <strong>{selected.modeLabel}</strong>
          </div>
          <p>{selected.reason}</p>
          <dl>
            <div><dt>代价</dt><dd>{selected.cost}</dd></div>
            <div><dt>预计耗时</dt><dd>{selected.durationDays ? `${selected.durationDays} 日` : "无法发出"}</dd></div>
            <div><dt>可能结果</dt><dd>{selected.expected}</dd></div>
          </dl>
        </div>

        <footer className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>暂不处理</button>
          <button type="button" className="primary-ink-button" disabled={selected.mode === "unavailable"} onClick={() => onSubmit(selected)}>
            {selected.mode === "direct" ? "亲自出工" : selected.mode === "request" ? "送出请求" : "缺少权限"}<ArrowRight size={17} />
          </button>
        </footer>
      </section>
    </div>
  );
}

function ResultModal({ result, resultDate, season, onClose, onReset }) {
  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section className="result-modal paper-panel" role="dialog" aria-modal="true" aria-labelledby="result-title" onMouseDown={(event) => event.stopPropagation()}>
        <header className="result-heading">
          <span className="result-seal">回报</span>
          <div><span className="eyebrow">{season} · {resultDate}</span><h2 id="result-title">{result.title}</h2></div>
          <IconButton label="关闭结果" onClick={onClose}><X size={21} /></IconButton>
        </header>
        <p className="result-summary">{result.summary}</p>
        <div className="result-effects">
          {result.effects.map((effect) => <div key={effect}><CheckCircle size={18} weight="fill" /><span>{effect}</span></div>)}
        </div>
        <div className="causal-chain" aria-label="因果链">
          {result.causalChain.map((item, index) => (
            <div key={item}>
              <span>{item}</span>{index < result.causalChain.length - 1 && <ArrowRight size={17} />}
            </div>
          ))}
        </div>
        <div className="result-note"><Eye size={18} /><span>你只知道{snapshot.person.name}公开答应了什么；他真正如何衡量此事仍不可见。</span></div>
        <footer className="modal-actions">
          <button type="button" className="secondary-button" onClick={onReset}>重新演示</button>
          <button type="button" className="primary-ink-button" onClick={onClose}>回到地图<ArrowRight size={17} /></button>
        </footer>
      </section>
    </div>
  );
}

function SourceModal({ selectedNode, onClose }) {
  const isDivision = selectedNode?.entityType === "division";
  const isCountySeat = selectedNode?.id === snapshot.countySeatPlan.id;
  const division = isDivision ? selectedNode : DIVISION_BY_ID.get(selectedNode?.divisionId);
  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section className="source-modal paper-panel" role="dialog" aria-modal="true" aria-labelledby="source-title" onMouseDown={(event) => event.stopPropagation()}>
        <header className="modal-heading">
          <div><span className="eyebrow">原型数据边界</span><h2 id="source-title">所见信息从何而来</h2></div>
          <IconButton label="关闭数据说明" onClick={onClose}><X size={21} /></IconButton>
        </header>
        <dl className="source-list">
          <div><dt>县域快照</dt><dd>{snapshot.metadata.county} · {division?.name ?? "县内地点"}</dd></div>
          <div><dt>地理可见性</dt><dd>当前只显示已知县域与所见地点；未探索的上级地理保持隐藏</dd></div>
          <div><dt>地点短号</dt><dd>{(selectedNode?.id ?? snapshot.focusVillage.id).split("-").at(-1)}</dd></div>
          {isDivision ? (
            <>
              <div><dt>计算层级</dt><dd>{selectedNode.typeLabel}；中心为{selectedNode.centerSettlementName}</dd></div>
              <div><dt>聚落规模</dt><dd>{selectedNode.settlementCount}处聚落，推定{selectedNode.residentPopulation.toLocaleString("zh-CN")}口</dd></div>
              <div><dt>证据边界</dt><dd>historical_name_claim=no；boundary_historical_claim=no</dd></div>
              <div><dt>并行资源区</dt><dd>{selectedNode.subregionName}，不是行政父级</dd></div>
            </>
          ) : isCountySeat ? (
            <>
              <div><dt>聚居人口</dt><dd>{snapshot.countySeatPlan.populationDisplay}，为县治聚居人口确定性投影，不是精确史实</dd></div>
              <div><dt>县治名称</dt><dd>行政驻地投影；只确认县治层级，不把生成街区包装成记载史实</dd></div>
              <div><dt>设施类型</dt><dd>县署、县学、集市、医馆、寺观、作坊、营堡与码头来自县治静态设施规划</dd></div>
            </>
          ) : (
            <>
              <div><dt>人口</dt><dd>{selectedNode?.residentPopulation ?? snapshot.focusVillage.projectedPopulation} 人，为县级人口权重推定显示值</dd></div>
              <div><dt>乡镇隶属</dt><dd>{division?.name ?? snapshot.focusVillage.divisionName}；{selectedNode?.membershipMethod ?? "确定性空间投影"}</dd></div>
              <div><dt>聚落名称</dt><dd>{selectedNode?.historicalNameClaim ? "有历史名称声明" : "时代风格生成名，historical_name_claim=no"}</dd></div>
            </>
          )}
          <div><dt>运行方式</dt><dd>静态 JSON 原型快照，不在浏览器中连接 SQLite</dd></div>
          <div><dt>许可边界</dt><dd>研究数据尚未达到商业发布条件，仅用于本地内部原型</dd></div>
        </dl>
        <div className="source-warning"><Warning size={20} weight="fill" /><span>界面会区分亲见、口述、推定、未知与过期信息，不把系统后台真相直接交给玩家。</span></div>
      </section>
    </div>
  );
}

function PlayerProfileModal({ stage, currentLocation, countyVisited, onClose, onOpenTravel }) {
  const isAtQili = currentLocation.id === snapshot.focusVillage.id;
  const isAtCountySeat = currentLocation.id === snapshot.countySeatPlan.id;
  const currentStatus = isAtQili
    ? stage === "resolved"
      ? "修渠已经开工，继续寻找人手"
      : stage === "pending"
        ? "已承诺出工，等待修渠回报"
        : "正在查看西田断水"
    : isAtCountySeat
      ? stage === "pending"
        ? "正在县城停留；修渠请求仍在等待回报"
        : stage === "resolved"
          ? "正在县城停留；修渠回报已经送达"
          : "正在南门内探看榜文与市情"
    : stage === "pending"
      ? `正在${currentLocation.name}停留；修渠请求仍在等待回报`
      : stage === "resolved"
        ? `正在${currentLocation.name}停留；修渠回报已经送达`
        : `正在${currentLocation.name}探看村情`;

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section className="player-profile-modal paper-panel" role="dialog" aria-modal="true" aria-labelledby="player-profile-title" onMouseDown={(event) => event.stopPropagation()}>
        <header className="modal-heading player-profile-heading">
          <div className="player-profile-identity">
            <span className="player-profile-model"><img src={snapshot.player.modelAsset} alt={`${snapshot.player.name}人物模型`} /></span>
            <div>
              <span className="eyebrow">我的人物 · 当前公开状态</span>
              <h2 id="player-profile-title">{snapshot.player.name}</h2>
              <div className="player-profile-tags"><span>{snapshot.player.identity}</span><span>{snapshot.player.householdRole}</span></div>
            </div>
          </div>
          <IconButton label="关闭个人属性" onClick={onClose}><X size={21} /></IconButton>
        </header>

        <div className="player-attribute-grid" aria-label="个人属性">
          <div><span>年龄</span><strong>{snapshot.player.age} 岁</strong></div>
          <div><span>当前地点</span><strong>{currentLocation.name} · {currentLocation.arrival}</strong></div>
          <div><span>已知区域</span><strong>{countyVisited ? `${snapshot.player.knownArea}、萧县城南门内` : snapshot.player.knownArea}</strong></div>
          <div><span>当前状态</span><strong>{currentStatus}</strong></div>
          <div><span>家庭人口</span><strong>{snapshot.player.householdPeople} 口</strong></div>
          <div><span>可用劳力</span><strong>{snapshot.player.availableLabor} 人</strong></div>
          <div><span>存粮</span><strong>{snapshot.player.grain}</strong></div>
          <div><span>现钱</span><strong>{snapshot.player.cash}</strong></div>
        </div>

        <section className="player-authority" aria-labelledby="player-authority-title">
          <div className="player-section-title"><Scales size={19} /><h3 id="player-authority-title">我能做什么</h3></div>
          <div className="authority-row authority-direct">
            <PersonSimpleRun size={20} />
            <span><strong>直接执行</strong><small>{snapshot.player.authority.self}</small></span>
          </div>
          <div className="authority-row authority-request">
            <Handshake size={20} />
            <span><strong>请求协商</strong><small>{snapshot.player.authority.cooperate}</small></span>
          </div>
          <div className="authority-row authority-unavailable">
            <LockKey size={20} />
            <span><strong>无权执行</strong><small>{snapshot.player.authority.requisition}</small></span>
          </div>
        </section>

        <div className="player-knowledge-note"><Eye size={18} /><span>这里只显示本人自知与公开属性；忠诚、野心等后台性格数值不会直接展示。</span></div>
        <footer className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>回到地图</button>
          <button type="button" className="primary-ink-button" onClick={onOpenTravel}>选择前往地点<PersonSimpleRun size={17} /></button>
        </footer>
      </section>
    </div>
  );
}

function TravelModal({ currentLocation, selectedDestinationId, setSelectedDestinationId, onClose, onConfirm }) {
  const destinations = KNOWN_DESTINATIONS;
  const destination = destinations.find((node) => node.id === selectedDestinationId) ?? destinations[0];
  const isCurrent = destination.id === currentLocation.id;
  const journey = assessTravel(currentLocation, destination);

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
      <section className="travel-modal paper-panel" role="dialog" aria-modal="true" aria-labelledby="travel-title" onMouseDown={(event) => event.stopPropagation()}>
        <header className="modal-heading travel-heading">
          <div><span className="eyebrow">县内行旅 · 已知地点</span><h2 id="travel-title">选择前往地点</h2></div>
          <IconButton label="关闭行旅面板" onClick={onClose}><X size={21} /></IconButton>
        </header>
        <p className="modal-intro">这里只列出你已经知道的县城与村庄。前往会推进时间，途中仍可收到重要回报。</p>

        <div className="travel-options" role="radiogroup" aria-label="可前往的县内地点">
          {destinations.map((place) => {
            const current = place.id === currentLocation.id;
            return (
              <button
                type="button"
                role="radio"
                aria-checked={selectedDestinationId === place.id}
                className={`travel-option ${selectedDestinationId === place.id ? "is-selected" : ""}`}
                key={place.id}
                onClick={() => setSelectedDestinationId(place.id)}
              >
                <span className={`travel-village-icon ${place.kind === "county_seat" ? "is-county-seat" : ""}`}><MapNodeIcon kind={place.kind} /></span>
                <span><strong>{place.name}</strong><small>{place.divisionName} · {place.arrival} · {place.population}</small></span>
                <em>{current ? "当前" : `${assessTravel(currentLocation, place).days} 日`}</em>
              </button>
            );
          })}
        </div>

        <div className="travel-assessment" aria-live="polite">
          <div className="travel-route-title"><MapPin size={20} weight="fill" /><strong>{currentLocation.name}</strong><ArrowRight size={18} /><strong>{destination.name}</strong></div>
          <dl>
            <div><dt>路程</dt><dd>{isCurrent ? "你已在此地" : `${journey.days} 日`}</dd></div>
            <div><dt>层级</dt><dd>{snapshot.metadata.county} → {destination.divisionName} → {destination.name}</dd></div>
            <div><dt>路线</dt><dd>{journey.route}</dd></div>
            <div><dt>行装</dt><dd>{journey.cost}</dd></div>
          </dl>
        </div>

        <footer className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>暂不动身</button>
          <button type="button" className="primary-ink-button" disabled={isCurrent} onClick={() => onConfirm(destination)}>
            {isCurrent ? "你已在这里" : `启程前往${destination.name}`}<PersonSimpleRun size={17} />
          </button>
        </footer>
      </section>
    </div>
  );
}

function Toast({ toast, onClose }) {
  if (!toast) return null;
  return (
    <div className={`toast toast-${toast.kind}`} role="status">
      {toast.kind === "locked" ? <LockKey size={18} /> : <Info size={18} />}
      <span>{toast.message}</span>
      <button type="button" aria-label="关闭提示" onClick={onClose}><X size={16} /></button>
    </div>
  );
}

export function Prototype() {
  const [paused, setPaused] = useState(true);
  const [speed, setSpeed] = useState(2);
  const [day, setDay] = useState(0);
  const [zoomLevel, setZoomLevel] = useState(2);
  const [selectedNode, setSelectedNode] = useState(FOCUS_VILLAGE);
  const [selectedDivisionId, setSelectedDivisionId] = useState(FOCUS_VILLAGE.divisionId);
  const [leftTab, setLeftTab] = useState("overview");
  const [rightTab, setRightTab] = useState("affairs");
  const [investigated, setInvestigated] = useState(false);
  const [stage, setStage] = useState("alert");
  const [actionOpen, setActionOpen] = useState(false);
  const [selectedActionId, setSelectedActionId] = useState("requestHeadman");
  const [pending, setPending] = useState(null);
  const [result, setResult] = useState(null);
  const [resultOpen, setResultOpen] = useState(false);
  const [sourceOpen, setSourceOpen] = useState(false);
  const [playerOpen, setPlayerOpen] = useState(false);
  const [travelOpen, setTravelOpen] = useState(false);
  const [currentLocationId, setCurrentLocationId] = useState(snapshot.player.initialLocationId);
  const [visitedLocationIds, setVisitedLocationIds] = useState([snapshot.player.initialLocationId]);
  const [selectedDestinationId, setSelectedDestinationId] = useState(snapshot.countySeatPlan.id);
  const [toast, setToastState] = useState(null);
  const [events, setEvents] = useState(snapshot.initialEvents);

  const setToast = useCallback((message, kind = "info") => {
    setToastState({ message, kind, id: Date.now() });
  }, []);

  useEffect(() => {
    if (!toast) return undefined;
    const timer = window.setTimeout(() => setToastState(null), 3600);
    return () => window.clearTimeout(timer);
  }, [toast]);

  const askAboutCanal = useCallback(() => {
    if (!investigated) {
      setInvestigated(true);
      setStage("investigated");
      setEvents((current) => [
        ...current,
        {
          id: "event-investigated",
          date: dateLabel(day),
          category: "查问",
          severity: "important",
          title: "已查明东渠淤塞",
          detail: `${snapshot.person.name}愿意召集乡邻，但要先知道谁肯出工。`,
        },
      ]);
      setToast(`你已完成查问：东渠淤塞位置和${snapshot.person.position}的打算已经明确。`, "info");
    } else {
      setToast(`${snapshot.person.name}说：若你家先出两名劳力，我便去问其余各户。`, "info");
    }
  }, [day, investigated, setToast]);

  const resolvePending = useCallback((activePending) => {
    const isRequest = activePending.actionId === "requestHeadman";
    const resultEventId = `event-result-${activePending.actionId}`;
    const nextResult = isRequest
      ? {
          status: "partial",
          title: "众户允诺开工，尚缺人手",
          summary: `${snapshot.person.name}走访各户后，十二户答应先出工三日，另有两户仍在观望。主渠已经开清一段，但西田尚不能全数恢复引水。`,
          effects: ["十二户答应出工", "东渠开始清淤", "西田水情由“断水”变为“恢复中”"],
          causalChain: ["你承诺先出工", `${snapshot.person.name}愿意出面`, "十二户响应", "主渠开始清淤"],
          eventIds: [resultEventId],
        }
      : {
          status: "partial",
          title: "支渠稍通，主渠仍塞",
          summary: "你独自清开了自家田边的支渠，但主渠淤塞过深，一人无法继续。乡邻看见你出工，却还没有形成共同约定。",
          effects: ["个人出工三日", "自家支渠稍有水势", "主渠仍需多人合力"],
          causalChain: ["你亲自出工", "支渠稍通", "主渠仍塞", "需要继续协商"],
          eventIds: [resultEventId],
        };

    setResult(nextResult);
    setStage("resolved");
    setPaused(true);
    setPending({ ...activePending, status: "resolved", currentDay: activePending.completesOnDay });
    setEvents((current) => [
      ...current,
      {
        id: resultEventId,
        date: dateLabel(activePending.completesOnDay),
        category: "民生",
        severity: "important",
        title: nextResult.title,
        detail: nextResult.effects.join("；"),
      },
    ]);
    setResultOpen(true);
  }, []);

  useEffect(() => {
    if (!pending || pending.status !== "waiting" || day < pending.completesOnDay) return;
    resolvePending(pending);
  }, [day, pending, resolvePending]);

  useEffect(() => {
    if (paused) return undefined;
    const timer = window.setInterval(() => setDay((current) => current + 1), Math.max(280, 1300 / speed));
    return () => window.clearInterval(timer);
  }, [paused, speed]);

  const submitAction = useCallback((action) => {
    const nextPending = {
      actionId: action.id,
      targetId: snapshot.focusVillage.id,
      executorId: action.id === "requestHeadman" ? snapshot.person.id : "PLAYER",
      submittedOnDay: day,
      completesOnDay: day + action.durationDays,
      currentDay: day,
      status: "waiting",
    };
    setPending(nextPending);
    setStage("pending");
    setActionOpen(false);
    setPaused(true);
    setEvents((current) => [
      ...current,
      {
        id: `event-submit-${action.id}`,
        date: dateLabel(day),
        category: action.mode === "request" ? "协商" : "劳作",
        severity: "important",
        title: action.mode === "request" ? "修渠请求已送出" : "你已带锄出工",
        detail: action.mode === "request" ? `${snapshot.person.name}将走访各户，三日后回报。` : "你开始清理自家田边支渠。",
      },
    ]);
    setToast(action.mode === "request" ? "请求已经送出。推进三日，等待世界回应。" : "你已开始出工。推进三日查看结果。", "info");
  }, [day, setToast]);

  const advanceDays = useCallback((count) => {
    if (!count) return;
    setDay((current) => current + count);
  }, []);

  const openTravel = useCallback((destinationId) => {
    const requestedDestination = KNOWN_DESTINATION_IDS.has(destinationId) ? destinationId : null;
    const fallbackDestination = KNOWN_DESTINATIONS.find((node) => node.id !== currentLocationId);
    setSelectedDestinationId(requestedDestination ?? fallbackDestination?.id ?? currentLocationId);
    setPlayerOpen(false);
    setTravelOpen(true);
  }, [currentLocationId]);

  const completeTravel = useCallback((destination) => {
    if (destination.id === currentLocationId) return;
    const origin = SETTLEMENT_BY_ID.get(currentLocationId);
    const journey = assessTravel(origin, destination);
    const arrivalDay = day + journey.days;
    setDay(arrivalDay);
    setPaused(true);
    setCurrentLocationId(destination.id);
    setSelectedDivisionId(destination.divisionId);
    setVisitedLocationIds((current) => current.includes(destination.id) ? current : [...current, destination.id]);
    setSelectedNode(destination);
    setLeftTab("overview");
    setZoomLevel(2);
    setTravelOpen(false);
    setEvents((current) => [
      ...current,
      {
        id: `event-travel-${destination.id}-${arrivalDay}`,
        date: dateLabel(arrivalDay),
        category: "行旅",
        severity: "normal",
        title: `抵达${destination.name}`,
        detail: `自${origin.name}出发，经${journey.route}，${journey.days}日后到达${destination.arrival}。`,
      },
    ]);
    setToast(`你已抵达${destination.name} · ${destination.arrival}。`, "info");
  }, [currentLocationId, day, setToast]);

  const openCountySeat = useCallback(() => {
    setSelectedNode(COUNTY_SEAT);
    setSelectedDivisionId(COUNTY_SEAT.divisionId);
    setLeftTab("overview");
    setZoomLevel(2);
    setToast("已在地图上选中萧县城。你可以先查看公开概览，再决定是否进城。", "info");
  }, [setToast]);

  const inspectCountyPlace = useCallback((place) => {
    if (currentLocationId !== snapshot.countySeatPlan.id) {
      setToast(`你记得${place.name}的位置；要当面办理，需先返回萧县城。`, "info");
      return;
    }
    const eventId = `event-county-place-${place.id ?? place.name}`;
    setEvents((current) => current.some((event) => event.id === eventId) ? current : [
      ...current,
      {
        id: eventId,
        date: dateLabel(day),
        category: "见闻",
        severity: "normal",
        title: `探看${place.name}`,
        detail: place.detail ?? place.known,
      },
    ]);
    setToast(`${place.name}：${place.detail ?? place.known}`, place.access === "不可擅入" ? "locked" : "info");
  }, [currentLocationId, day, setToast]);

  const resetScenario = useCallback(() => {
    setPaused(true);
    setSpeed(2);
    setDay(0);
    setZoomLevel(2);
    setSelectedNode(FOCUS_VILLAGE);
    setSelectedDivisionId(FOCUS_VILLAGE.divisionId);
    setLeftTab("overview");
    setRightTab("affairs");
    setInvestigated(false);
    setStage("alert");
    setActionOpen(false);
    setSelectedActionId("requestHeadman");
    setPending(null);
    setResult(null);
    setResultOpen(false);
    setPlayerOpen(false);
    setTravelOpen(false);
    setCurrentLocationId(snapshot.player.initialLocationId);
    setVisitedLocationIds([snapshot.player.initialLocationId]);
    setSelectedDestinationId(snapshot.countySeatPlan.id);
    setEvents(snapshot.initialEvents);
    setToast("原型已经重置到四月初三。", "info");
  }, [setToast]);

  useEffect(() => {
    const onKeyDown = (event) => {
      if (event.target.closest("input, textarea, select")) return;
      if (event.code === "Space") {
        event.preventDefault();
        setPaused((current) => !current);
      }
      if (["Digit1", "Digit2", "Digit3"].includes(event.code)) setSpeed(SPEEDS[Number(event.code.at(-1)) - 1]);
      if (event.code === "Escape") {
        setActionOpen(false);
        setResultOpen(false);
        setSourceOpen(false);
        setPlayerOpen(false);
        setTravelOpen(false);
        setToastState(null);
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  const currentPending = useMemo(() => pending ? { ...pending, currentDay: day } : null, [pending, day]);
  const currentLocation = useMemo(() => SETTLEMENT_BY_ID.get(currentLocationId) ?? FOCUS_VILLAGE, [currentLocationId]);
  const countyVisited = visitedLocationIds.includes(snapshot.countySeatPlan.id);
  const villageStatus = stage === "resolved" ? "repairing" : stage === "pending" ? "waiting" : "blocked";
  const ribbonMessage = stage === "resolved"
    ? `重要回报已到，世界自动暂停：${result?.title ?? "修渠进展"}`
    : stage === "pending"
      ? "修渠请求已送出：世界保持暂停，可推进三日等待回报"
      : investigated
        ? "重大事件已暂停世界：已确认七里村东渠淤塞"
        : "重大事件已暂停世界：七里村西田断水";

  return (
    <main className="realm-app">
      <header className="topbar paper-panel">
        <div className="brand-seal" aria-label="Project Realm 原型"><Seal size={34} weight="duotone" /></div>
        <div className="date-block">
          <strong>{snapshot.metadata.season}</strong>
          <span>{dateLabel(day)}　巳时</span>
        </div>
        <div className="time-controls" aria-label="时间控制">
          <IconButton label={paused ? "继续时间（空格）" : "暂停时间（空格）"} active={!paused} onClick={() => setPaused(!paused)}>
            {paused ? <Play size={18} weight="fill" /> : <Pause size={18} weight="fill" />}
          </IconButton>
          {SPEEDS.map((value, index) => (
            <button type="button" key={value} className={`speed-button ${speed === value ? "is-active" : ""}`} onClick={() => setSpeed(value)} aria-label={`${value} 倍速度，快捷键 ${index + 1}`}>
              {value === 1 ? <Play size={15} /> : <FastForward size={15} weight={value === 4 ? "fill" : "regular"} />}<span>x{value}</span>
            </button>
          ))}
        </div>
        <div className="location-breadcrumb" aria-label="我的当前位置">
          <strong>{snapshot.metadata.county}</strong><CaretRight size={16} />
          <strong>{currentLocation.divisionName}</strong><CaretRight size={16} />
          <strong>{currentLocation.name}</strong>
        </div>
        <div className="household-stats" aria-label="玩家家庭资源">
          <div title="玩家家庭人口"><UsersThree size={19} /><span><small>本户</small>{snapshot.player.householdPeople} 口</span></div>
          <div title="本户存粮"><Grains size={19} /><span><small>存粮</small>{snapshot.player.grain}</span></div>
          <div title="本户现钱"><Coins size={19} /><span><small>现钱</small>{snapshot.player.cash}</span></div>
          <div title="今日可用劳力"><Toolbox size={19} /><span><small>劳力</small>{snapshot.player.availableLabor} 人</span></div>
        </div>
        <div className="weather-block"><Sun size={24} /><span><strong>少雨　22℃</strong><small>河水偏低</small></span></div>
        <IconButton label="重置演示" onClick={resetScenario} className="reset-button"><Gear size={19} /></IconButton>
      </header>

      <div className="workspace-grid">
        <LeftPanel
          tab={leftTab}
          setTab={setLeftTab}
          selectedNode={selectedNode}
          currentLocationId={currentLocationId}
          countyVisited={countyVisited}
          investigated={investigated}
          stage={stage}
          onAsk={askAboutCanal}
          onOpenActions={() => setActionOpen(true)}
          onOpenSource={() => setSourceOpen(true)}
          onOpenTravel={openTravel}
          onCountyPlace={inspectCountyPlace}
          onOpenDivision={(division) => {
            setSelectedDivisionId(division.id);
            setZoomLevel(2);
            setToast(`已展开${division.name}的${division.settlementCount}处聚落。`, "info");
          }}
          onExplainUnknownRoute={(place) => setToast(`${place.name}已在区域数据中，但你尚未获得可靠路线；先询问或到达${place.divisionName}中心。`, "locked")}
        />

        <MapViewport
          zoomLevel={zoomLevel}
          setZoomLevel={setZoomLevel}
          selectedNodeId={selectedNode.id}
          selectedDivisionId={selectedDivisionId}
          onSelectNode={(node) => {
            setSelectedNode(node);
            setSelectedDivisionId(node.divisionId);
            setLeftTab("overview");
          }}
          onSelectDivision={(division) => {
            setSelectedNode(division);
            setSelectedDivisionId(division.id);
            setLeftTab("overview");
            setZoomLevel(2);
            setToast(`已展开${division.name}的${division.settlementCount}处聚落；村落是地图的最小世界单元。`, "info");
          }}
          villageStatus={villageStatus}
          playerOpen={playerOpen}
          travelOpen={travelOpen}
          playerLocation={currentLocation}
          onOpenPlayer={() => {
            setSelectedNode(currentLocation);
            setLeftTab("overview");
            setPlayerOpen(true);
          }}
        />

        <RightPanel
          tab={rightTab}
          setTab={setRightTab}
          investigated={investigated}
          stage={stage}
          pending={currentPending}
          result={result}
          onAsk={askAboutCanal}
          onOpenActions={() => setActionOpen(true)}
          onShowResult={() => setResultOpen(true)}
          onOpenCounty={openCountySeat}
          setToast={setToast}
        />

        <Chronicle
          events={events}
          stage={stage}
          pending={currentPending}
          day={day}
          paused={paused}
          setPaused={setPaused}
          advanceDays={advanceDays}
          onShowResult={() => setResultOpen(true)}
        />
      </div>

      <div className="prototype-ribbon" role="status"><Bell size={15} weight="fill" /><span>{ribbonMessage}</span></div>

      {actionOpen && (
        <ActionSheet
          selectedActionId={selectedActionId}
          setSelectedActionId={setSelectedActionId}
          onClose={() => setActionOpen(false)}
          onSubmit={submitAction}
        />
      )}
      {resultOpen && result && <ResultModal result={result} resultDate={dateLabel(day)} season={snapshot.metadata.season} onClose={() => setResultOpen(false)} onReset={resetScenario} />}
      {sourceOpen && <SourceModal selectedNode={selectedNode} onClose={() => setSourceOpen(false)} />}
      {playerOpen && <PlayerProfileModal stage={stage} currentLocation={currentLocation} countyVisited={countyVisited} onClose={() => setPlayerOpen(false)} onOpenTravel={() => openTravel()} />}
      {travelOpen && <TravelModal currentLocation={currentLocation} selectedDestinationId={selectedDestinationId} setSelectedDestinationId={setSelectedDestinationId} onClose={() => setTravelOpen(false)} onConfirm={completeTravel} />}
      <Toast toast={toast} onClose={() => setToastState(null)} />
    </main>
  );
}
