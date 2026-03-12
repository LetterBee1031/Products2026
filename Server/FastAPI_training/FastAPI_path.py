from fastapi import FastAPI
from enum import Enum

app = FastAPI()

@app.get("/")
async def root():
    return {"message": "Hello World"}

@app.get("/items/{item_id}")
async def read_item(item_id: int):
    return {"item_id": item_id}

# 先に記述したほうが先に処理されるから，user_id = me の人を作れないようになる
# 同様に同じpath operationで2つ以上作っても一番最初のやつに全部行く
@app.get("/users/me")
async def read_user_me():
    return {"user_id": "the current user"}

@app.get("/users/{user_id}")
async def read_user(user_id: str):
    return{"user_id": user_id}

# Enumは列挙型ってやつらしい　浅い理解ではマクロ定義をクラスでやる感じに近い？　こういう記述したほうがいいのはなんとなくわかるけど，普通にクラス作るのとなにが違うのかしら？

class ModelName(str, Enum):
    alexnet = "alexnet"
    resnet = "resnet"
    lenet = "lenet"

@app.get("/models/{model_name}")
async def get_model(model_name: ModelName):
    if model_name is ModelName.alexnet:
        return {"model_name": model_name, "message": "Deep Learning FTW!"}
    
    if model_name.value == "lenet":
        return {"model_name": model_name, "message": "LeCNN all the images"}
    
    return {"model_name": model_name, "message": "Have some residuals"}