from fastapi import FastAPI

# クエリの話
app = FastAPI()
fake_items_db = [{"item_name": "Foo"}, {"item_name": "Bar"}, {"item_name": "Baz"}]

# 
# http://127.0.0.1:8000/items/?skip=0&limit=10
@app.get("/items/")
async def read_item(skip: int = 0, limit: int = 10):
    return fake_items_db[skip : skip + limit]

# 真偽値も送れる
# http://127.0.0.1:8000/items/foo?short=True
@app.get("/items/{item_id}")
async def read_item(item_id: str, q: str | None = None, short: bool = False):
    item = {"item_id": item_id}
    if q:
        item.update
        return {"item_id": item_id, "q": q}
    return {"item_id": item_id}

# 初期値を宣言しないと必須パラメータになる，宣言しておけば必須じゃなくなる
# http://127.0.0.1:8000/items2/baker?needy=needyGirl
@app.get("/items2/{item_id}")
async def read_user_item(item_id: str, needy: str):
    item = {"item_id": item_id, "needy": needy}
    return item

# http://127.0.0.1:8000/items2/baker?needy=needyGirl
@app.get("/items3/{item_id}")
async def read_user_item(
    item_id: str, needy: str, skip: int = 0, limit: int | None = None
):
    item = {"item_id": item_id, "needy": needy}
    return item

# 複数のパスパラメータを設定でききる
# http://127.0.0.1:8000/users/0032/items/happy
@app.get("/users/{user_id}/items/{item_id}")
async def read_user_item(
    user_id: int, item_id: str, q: str | None = None, short: bool = False
):
    item = {"item_id": item_id, "owner_id": user_id}
    if q:
        item.update({"q": q})
    if not short:
        item.update(
            {"description": "This is an amazing item that has a long description"}
        )
    return item