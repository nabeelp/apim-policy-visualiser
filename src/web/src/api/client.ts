export interface ApiProblem {
  detail?: string;
  status?: number;
  title?: string;
}

export class ApiError extends Error {
  readonly status: number;
  readonly problem?: ApiProblem;

  constructor(message: string, status: number, problem?: ApiProblem) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.problem = problem;
  }
}

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/+$/, "");

function buildApiUrl(path: string): string {
  const normalizedPath = path.startsWith("/") ? path : `/${path}`;
  return `${apiBaseUrl}${normalizedPath}`;
}

async function readError(response: Response): Promise<ApiProblem | undefined> {
  const body = (await response.text()).trim();
  if (!body) {
    return undefined;
  }

  const contentType = response.headers.get("content-type") ?? "";
  if (contentType.includes("json")) {
    try {
      const parsed = JSON.parse(body) as ApiProblem;
      if (parsed && typeof parsed === "object") {
        return parsed;
      }
    } catch {
      // Some proxies return a text error while retaining an application/json
      // content type. Preserve that descriptive body instead of masking it with
      // a JSON parse exception.
    }
  }

  return { detail: body };
}

export async function apiFetch<T>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");

  const response = await fetch(buildApiUrl(path), {
    ...init,
    headers,
  });

  if (!response.ok) {
    const problem = await readError(response);
    const message =
      problem?.detail ??
      problem?.title ??
      `The API request failed with status ${response.status}.`;
    throw new ApiError(message, response.status, problem);
  }

  try {
    return (await response.json()) as T;
  } catch {
    throw new ApiError(
      "The API returned a successful response that could not be read as JSON.",
      response.status,
      { title: "Invalid API response" },
    );
  }
}
