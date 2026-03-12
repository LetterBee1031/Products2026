from typing import Annotated
from fastapi import FastAPI, Query

import random
from pydantic import AfterValidator
app = FastAPI()

# Annotatedは第一引数に追加情報を付与する
# Queryはクエリに関して色々するやつらしい
@app.get("/items/")
async def read_items(q: Annotated[str | None, Query(min_length=3, max_length=50)] = "fixedquery"):
    results = {"items": [{"item_id": "Foo"},{"item_id": "Bar"}]}
    if q:
        results.update({"q": q})
    return results

# 正規表現のやつ
@app.get("/items2/")
async def read_items(q: Annotated[
        str | None, Query(min_length=3, max_length=50, pattern="^fixedquery$")
    ] = None
):
    results = {"items": [{"item_id": "Foo"},{"item_id": "Bar"}]}
    if q:
        results.update({"q": q})
    return results

# qを必須項目にするならこう
@app.get("/items3/")
async def read_items(q: Annotated[str | None, Query(min_length=3, max_length=50)]):
    results = {"items": [{"item_id": "Foo"},{"item_id": "Bar"}]}
    if q:
        results.update({"q": q})
    return results

# クエリをリスト化
@app.get("/items4/")
async def read_items(q: Annotated[list[str] | None, Query()] = ["foo", "bar"]):
    query_items = {"q":q}
    return query_items

# リストのパラメータの型を指定しないこともできる
@app.get("/items5/")
async def read_items(q: Annotated[list | None, Query()] = []):
    query_items = {"q":q}
    return query_items

# 説明文とかいろいろ情報を足せるらしい
@app.get("/items6/")
async def read_items(
    q: Annotated[
        str | None,
        Query(
            alias="item-query",
            title="Query String",
            description="Query string for the items to search in the database that have a good match",
            pattern="^fixedquery$",
            min_length=3,
            max_length=50,
            deprecated=True,
        ),
    ] = None
):
    results = {"items": [{"item_id": "Foo"}, {"item_id": "Bar"}]}
    if q: 
        results.update({"q": q})
    return results

# /docs/で見れるページでクエリの存在を隠せる
@app.get("/items7/")
async def read_items(
    hidden_query: Annotated[str | None, Query(include_in_schema=False)] = None
):
    if hidden_query:
        return {"hidden_query": hidden_query}
    else:
        return {"hidden_query": "Not fonud"}

data = {
    "isbn-9781529046137": "The Hitchhiker's Guide to the Galaxy",
    "imdb-tt0371724": "The Hitchhiker's Guide to the Galaxy",
    "isbn-9781439512982": "Isaac Asimov: The Complete Stories, Vol. 2",
}

def check_valid_id(id: str):
    if not id.startswith(("isbn-", "imdb-")):
        raise ValueError('Invald ID format, it must start with "isbn-" or "imdb-"')
    return id

@app.get("/items8/")
async def read_items(
    id: Annotated[str | None, AfterValidator(check_valid_id)] =None,
):
    if id:
        item = data.get(id)
    else:
        id, item = random.choice(list(data.items()))
    return {"id": id, "name": item}