---
description: CodeWiki Neo4j 그래프로 Markdown 문서 생성 (영향도/E2E 추적/VM 도시에 등)
argument-hint: 원하는 결과물 설명 (예: SearchOrderView E2E 문서, OrderDTO 영향도 리포트)
---

`codewiki-graph-doc` 스킬을 사용해 아래 요청을 처리하라. 스킬 지침대로 Neo4j 그래프를
`mcp__neo4j__read_neo4j_cypher`로 질의하고, 결과를 구조화된 Markdown(Mermaid + 표 +
근거 Cypher)으로 조립해 `docs/graphDoc/<주제>.md`에 저장한다.

요청: $ARGUMENTS

요청이 비어 있으면, 어떤 결과물을 원하는지(영향도 / 화면→DB E2E 추적 / ViewModel 도시에 /
호출 트리 / 서비스 인벤토리 / 구조 개요)와 대상 식별자(타입·View·ViewModel·서비스·엔티티
이름)를 한 줄로 물어라.
