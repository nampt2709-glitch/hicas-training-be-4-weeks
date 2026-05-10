/**
 * Parse login JSON envelope from CommentAPI / ApartmentAPI (camelCase or PascalCase).
 */
export function accessTokenFromLoginResponse(res) {
  let body;
  try {
    body = JSON.parse(res.body || '{}');
  } catch (e) {
    throw new Error(`Login response is not JSON (status ${res.status}): ${String(res.body).substring(0, 300)}`);
  }
  const data = body.data ?? body.Data;
  const token = data && (data.accessToken ?? data.AccessToken);
  if (!token) {
    throw new Error(`Login JSON missing data.accessToken. Keys: ${Object.keys(body).join(',')}. Snippet: ${String(res.body).substring(0, 400)}`);
  }
  return String(token);
}
