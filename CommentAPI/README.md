# CommentAPI

Project sample includes:
- CRUD Users
- CRUD Posts
- CRUD Comments
- Flat comment query
- Tree build in backend
- Recursive CTE comment query

## Important endpoints

### Comments
- `GET /api/comments/{id}`
- `POST /api/comments`
- `PUT /api/comments/{id}`
- `DELETE /api/comments/{id}`
- `GET /api/comments/post/{postId}/flat`
- `GET /api/comments/post/{postId}/tree`
- `GET /api/comments/post/{postId}/tree/cte`

### Posts
- `GET /api/posts`
- `GET /api/posts/{id}`
- `POST /api/posts`
- `PUT /api/posts/{id}`
- `DELETE /api/posts/{id}`

### Users
- `GET /api/users`
- `GET /api/users/{id}`
- `POST /api/users`
- `PUT /api/users/{id}`
- `DELETE /api/users/{id}`
