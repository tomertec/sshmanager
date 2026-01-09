# Context7 MCP Integration Architecture Plan

## Executive Summary

This document provides a comprehensive analysis of Context7 MCP (Model Context Protocol) best practices and evaluates the current implementation against official documentation. The research reveals that while the current implementation uses the core tools correctly, there are significant opportunities for improvement in parameter usage, error handling, library selection strategies, and leveraging advanced features like topic filtering, version-specific queries, and pagination. Key findings indicate that the current tool definitions are missing critical parameters that could dramatically improve documentation retrieval quality.

## Research Findings

### Current Implementation Analysis

The current project exposes two Context7 MCP tools:

1. **`mcp__context7__resolve-library-id`**
   - Parameters exposed: `libraryName` (required), `query` (required)
   - Purpose: Resolves package names to Context7-compatible library IDs

2. **`mcp__context7__query-docs`**
   - Parameters exposed: `libraryId` (required), `query` (required)
   - Purpose: Retrieves documentation and code examples for libraries

### Official Tool Specifications (from Context7 Documentation)

#### resolve-library-id Tool
The official tool accepts:
- **libraryName** (string, required): The name of the library or package to resolve

Response includes for each library:
- Library ID (format: `/org/project`)
- Name and Description
- Code Snippets count
- Source Reputation (High, Medium, Low, Unknown)
- Benchmark Score (0-100, quality indicator)
- Available Versions (format: `/org/project/version`)

#### get-library-docs (query-docs) Tool
The official tool accepts:
- **context7CompatibleLibraryID** (string, required): The library ID (e.g., `/vercel/next.js`)
- **topic** (string, optional): Filter documentation by specific topic
- **page** (integer, optional): Page number for pagination (defaults to 1)

### Best Practices from Official Documentation

#### 1. Library Selection Criteria
When selecting from multiple library matches:
- Prioritize **name similarity** to the query (exact matches first)
- Consider **Source Reputation** (prefer High or Medium)
- Evaluate **Code Snippets count** (higher = more examples)
- Check **Benchmark Score** (100 is highest quality)
- Match relevance to specific use case

#### 2. Topic Filtering
Always use the `topic` parameter when querying documentation:
```
topic: "routing"     // Focus on routing documentation
topic: "authentication"  // Focus on auth patterns
topic: "hooks"      // Focus on React hooks
```
Benefits:
- Reduces unnecessary content
- Improves response relevance
- Decreases token usage

#### 3. Version-Specific Queries
Use version-specific library IDs for consistent results:
```
/vercel/next.js/v15.1.8    // Specific version
/facebook/react/18.0.0      // Specific React version
```
Benefits:
- Consistent documentation across deployments
- Avoids breaking changes from version updates
- Matches project dependencies

#### 4. Pagination Strategy
For comprehensive documentation retrieval:
- Start with page 1
- Check `hasNext` in response
- Limit to necessary pages (max 10 pages / 100 snippets per topic)
- Use appropriate `limit` values

#### 5. Error Handling
Implement robust error handling for:
| Code | Description | Action |
|------|-------------|--------|
| 200 | Success | Process normally |
| 401 | Unauthorized | Check API key |
| 404 | Not Found | Verify library ID |
| 429 | Rate Limited | Implement exponential backoff |
| 500 | Server Error | Retry with backoff |

Rate limit responses include `retryAfterSeconds` field for retry timing.

#### 6. Token Management
Control documentation size with token limits:
```
tokens=2000   // Limit response to 2000 tokens
tokens=5000   // Larger context for complex queries
```

### Gaps in Current Implementation

#### Critical Gaps

1. **Missing `topic` Parameter in query-docs**
   - Current: Only `libraryId` and `query` exposed
   - Should have: `topic` parameter for focused documentation retrieval
   - Impact: Less relevant results, higher token usage

2. **Missing `page` Parameter in query-docs**
   - Current: No pagination support
   - Should have: `page` parameter for comprehensive retrieval
   - Impact: May miss important documentation beyond first page

3. **Missing Version Support Guidance**
   - Current: No guidance on using versioned library IDs
   - Should have: Clear instruction to use `/org/project/version` format
   - Impact: May get inconsistent results across sessions

4. **No Rate Limit Handling Strategy**
   - Current: No documented retry strategy
   - Should have: Exponential backoff implementation
   - Impact: May fail silently on rate limits

#### Minor Gaps

5. **Query Parameter in resolve-library-id**
   - Current: Has `query` parameter which is non-standard
   - Official: Only `libraryName` is specified
   - The `query` parameter appears to be a project-specific enhancement for ranking

6. **Missing Documentation Mode Selection**
   - Official API supports: `mode: "code"` or `mode: "info"`
   - Current: No mode selection
   - Impact: May not get optimal documentation type

7. **Missing Format Selection**
   - Official API supports: `format: "json"` or `format: "txt"`
   - Current: No format selection
   - Impact: Less control over response structure

### Additional Context7 Features Not Currently Used

1. **TypeScript/Python SDK Integration**
   - Official SDKs available: `@upstash/context7-sdk`
   - Provides type-safe access to all features
   - Supports advanced pagination and filtering

2. **Search Library Function**
   - `searchLibrary(query)` - finds libraries by name
   - Returns trust scores, star counts, processing states
   - Could enhance library discovery

3. **Private Repository Access**
   - API key enables private repo documentation
   - Team authentication levels available
   - Not currently documented for use

4. **LLM Context Files**
   - Libraries expose `llms.txt` files with optimized prompts
   - Available at: `https://context7.com/{org}/{project}/llms.txt`
   - Could enhance prompt engineering

## Proposed Architecture

### Component Structure

```
Context7 MCP Integration
|
+-- resolve-library-id
|   |-- Input: libraryName, query (for ranking)
|   |-- Output: Library matches with metadata
|   +-- Selection Logic: reputation > benchmark > snippets
|
+-- query-docs (enhanced)
|   |-- Input: libraryId, query, topic, page
|   |-- Output: Paginated documentation
|   +-- Features: version support, topic filtering
|
+-- Error Handler
|   |-- Rate limit detection (429)
|   |-- Exponential backoff
|   +-- Retry logic with configurable attempts
|
+-- Response Processor
    |-- Token counting
    |-- Pagination handling
    +-- Result aggregation
```

### Data Flow

1. **Library Resolution Flow**
   ```
   User Query -> Extract Library Name -> resolve-library-id
       -> Receive Matches -> Apply Selection Criteria
       -> Return Best Match with Version
   ```

2. **Documentation Retrieval Flow**
   ```
   Library ID + Topic -> query-docs (page 1)
       -> Process Results -> Check hasNext
       -> [If more pages needed] -> query-docs (page N)
       -> Aggregate Results -> Return to User
   ```

3. **Error Recovery Flow**
   ```
   API Call -> [429 Rate Limit]
       -> Extract retryAfterSeconds
       -> Wait (exponential backoff)
       -> Retry (max 3 attempts)
       -> [Success] Return Results
       -> [Failure] Return Error with Context
   ```

### Integration Points

1. **Tool Definition Updates**
   - Add `topic` parameter to query-docs
   - Add `page` parameter to query-docs
   - Update descriptions with version format guidance

2. **Configuration Enhancement**
   - API key support for higher rate limits
   - Configurable retry behavior
   - Token limit defaults

3. **Usage Guidelines**
   - Auto-invoke rules for code-related prompts
   - Library selection decision tree
   - Version pinning recommendations

## Implementation Roadmap

### Phase 1: Tool Definition Updates (Priority: High)

**Objective**: Align tool definitions with official API specifications

1. Update `query-docs` tool definition:
   ```json
   {
     "parameters": {
       "libraryId": {
         "description": "Exact Context7-compatible library ID (e.g., '/mongodb/docs', '/vercel/next.js', '/vercel/next.js/v14.3.0-canary.87')",
         "type": "string",
         "required": true
       },
       "query": {
         "description": "The question or task you need help with. Be specific and include relevant details.",
         "type": "string",
         "required": true
       },
       "topic": {
         "description": "Optional topic to filter documentation (e.g., 'routing', 'authentication', 'hooks')",
         "type": "string",
         "required": false
       },
       "page": {
         "description": "Page number for pagination (defaults to 1). Use when documentation spans multiple pages.",
         "type": "integer",
         "required": false
       }
     }
   }
   ```

2. Update tool descriptions with:
   - Version format examples (`/org/project/version`)
   - Topic filtering guidance
   - Pagination best practices

### Phase 2: Usage Guidelines (Priority: High)

**Objective**: Establish consistent usage patterns

1. Create library selection decision tree:
   ```
   Is there an exact name match?
     YES -> Check Source Reputation
       High/Medium -> Use this library
       Low/Unknown -> Consider alternatives with higher reputation
     NO -> Compare by:
       1. Benchmark Score (higher = better)
       2. Code Snippets count (more = better coverage)
       3. Description relevance to query
   ```

2. Define topic extraction rules:
   - Extract key concepts from user query
   - Map to common topic categories
   - Fall back to general query if no clear topic

3. Version strategy:
   - Check user's project dependencies when possible
   - Default to latest stable version
   - Document version in context for consistency

### Phase 3: Error Handling Enhancement (Priority: Medium)

**Objective**: Implement robust retry and fallback logic

1. Rate limit handling:
   ```python
   # Pseudocode for retry strategy
   def query_with_retry(params, max_retries=3):
       for attempt in range(max_retries):
           response = query_docs(params)
           if response.status == 429:
               wait_time = response.retryAfterSeconds or (2 ** attempt * 30)
               sleep(wait_time)
               continue
           return response
       raise MaxRetriesExceeded()
   ```

2. Fallback strategies:
   - If specific version fails, try latest
   - If topic filter returns empty, try without topic
   - If library not found, suggest similar libraries

### Phase 4: Advanced Features (Priority: Low)

**Objective**: Leverage additional Context7 capabilities

1. Token optimization:
   - Track token usage across queries
   - Implement token budgeting per session
   - Use token limits for large documentation sets

2. Documentation mode selection:
   - Use `mode: "code"` for implementation examples
   - Use `mode: "info"` for conceptual understanding
   - Auto-select based on query type

3. Private repository support:
   - Document API key configuration
   - Enable team authentication
   - Access private org documentation

## Risk Assessment

### High Risk

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Rate limiting disrupts workflow | Medium | High | Implement exponential backoff, use API key for higher limits |
| Incorrect library selection | Medium | High | Use selection criteria, verify with user for ambiguous cases |
| Outdated documentation retrieved | Low | High | Use version-specific queries, check library lastUpdateDate |

### Medium Risk

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Missing critical documentation | Medium | Medium | Use pagination, multiple topic queries |
| API breaking changes | Low | Medium | Monitor Context7 changelog, version lock SDK |
| Token budget exceeded | Medium | Medium | Implement token limits, use topic filtering |

### Low Risk

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Library not in Context7 | Low | Low | Fall back to web search, note limitation |
| Slow response times | Low | Low | Cache frequent queries, use local MCP server |

## Recommendations

### Immediate Actions (This Week)

1. **Add `topic` parameter to query-docs tool**
   - Highest impact improvement for documentation relevance
   - Reduces token usage significantly
   - Simple configuration change

2. **Update tool descriptions with version format**
   - Guide users to use `/org/project/version` format
   - Include examples in descriptions
   - Note latest version availability

3. **Add call limit warnings**
   - Current limit: 3 calls per question
   - Document this clearly in tool descriptions
   - Plan queries to maximize value within limits

### Short-term Actions (This Month)

4. **Implement pagination support**
   - Add `page` parameter to query-docs
   - Document pagination strategy in guidelines
   - Handle multi-page responses gracefully

5. **Create library selection guidelines**
   - Document selection criteria hierarchy
   - Provide examples for common scenarios
   - Handle ambiguous library names

6. **Add auto-invoke configuration**
   - Create CLAUDE.md rule for Context7 usage
   - Trigger on code-related queries automatically
   - Reduce explicit "use context7" requirements

### Long-term Actions (This Quarter)

7. **Consider SDK integration**
   - Evaluate TypeScript SDK for advanced features
   - Enable type-safe API access
   - Access pagination metadata directly

8. **Implement rate limit handling**
   - Add exponential backoff strategy
   - Track and report rate limit encounters
   - Consider API key for production use

9. **Monitor and optimize token usage**
   - Track tokens per query type
   - Optimize default token limits
   - Report on documentation coverage

## Configuration Recommendations

### Recommended Tool Definition Update

```json
{
  "name": "mcp__context7__query-docs",
  "description": "Retrieves and queries up-to-date documentation and code examples from Context7 for any programming library or framework.\n\nYou must call 'resolve-library-id' first to obtain the exact Context7-compatible library ID required to use this tool, UNLESS the user explicitly provides a library ID in the format '/org/project' or '/org/project/version' in their query.\n\nBest practices:\n- Use the 'topic' parameter to filter for relevant documentation (e.g., 'routing', 'authentication')\n- Use version-specific IDs for consistent results (e.g., '/vercel/next.js/v15.1.8')\n- Use 'page' parameter for comprehensive documentation retrieval\n\nIMPORTANT: Do not call this tool more than 3 times per question. If you cannot find what you need after 3 calls, use the best information you have.",
  "parameters": {
    "libraryId": {
      "type": "string",
      "description": "Exact Context7-compatible library ID (e.g., '/mongodb/docs', '/vercel/next.js', '/supabase/supabase', '/vercel/next.js/v14.3.0-canary.87') retrieved from 'resolve-library-id' or directly from user query in the format '/org/project' or '/org/project/version'.",
      "required": true
    },
    "query": {
      "type": "string",
      "description": "The question or task you need help with. Be specific and include relevant details. Good: 'How to set up authentication with JWT in Express.js' or 'React useEffect cleanup function examples'. Bad: 'auth' or 'hooks'. IMPORTANT: Do not include any sensitive or confidential information such as API keys, passwords, credentials, or personal data in your query.",
      "required": true
    },
    "topic": {
      "type": "string",
      "description": "Optional topic to filter documentation for more relevant results (e.g., 'routing', 'authentication', 'hooks', 'api', 'configuration'). Reduces token usage and improves accuracy.",
      "required": false
    },
    "page": {
      "type": "integer",
      "description": "Page number for pagination (defaults to 1). Use for comprehensive documentation retrieval when initial results are insufficient. Maximum 10 pages per topic.",
      "required": false
    }
  }
}
```

### Recommended Auto-invoke Rule (CLAUDE.md)

```markdown
## Context7 MCP Usage

When working with code generation, setup, configuration, or documentation queries:
1. Always use Context7 MCP tools to fetch up-to-date library documentation
2. First call `resolve-library-id` to get the correct library ID unless already known
3. Use specific topics and versions for focused, accurate results
4. Limit to 3 tool calls per question - plan queries to maximize coverage
```

## Appendix: API Reference Summary

### Rate Limits

| Authentication | Limit | Reset Window |
|----------------|-------|--------------|
| No API Key | Low (exact limit varies) | Per minute |
| With API Key | Higher based on plan | Dashboard visible |

### Error Codes

| Code | Meaning | Retry |
|------|---------|-------|
| 200 | Success | N/A |
| 401 | Invalid API key | No - fix auth |
| 404 | Library not found | No - verify ID |
| 429 | Rate limited | Yes - use retryAfterSeconds |
| 500 | Server error | Yes - exponential backoff |

### Library ID Formats

| Format | Example | Use Case |
|--------|---------|----------|
| `/org/project` | `/vercel/next.js` | Latest version |
| `/org/project/version` | `/vercel/next.js/v15.1.8` | Specific version |
| `/websites/domain` | `/websites/context7_mintlify_dev` | Website documentation |

---

*Research conducted: 2026-01-04*
*Sources: Context7 Official Documentation, Context7 MCP Server Repository*
