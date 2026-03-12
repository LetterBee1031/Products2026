from typing import List
from datetime import datetime
from pydantic import BaseModel

def get_full_name(first_name: str, last_name: str):
    # string.titleは頭文字だけ大文字にするメソッドらしい
    full_name = first_name.title() + " " + last_name.title()
    return full_name

def get_name_with_age(name: str, age: int):
    name_with_age = name + "is this old: " + str(age)
    return name_with_age

def process_items(items: List[str]):
    for item in items:
        print(item)
    
class User(BaseModel):
    id: int
    name: str = "John Doe"
    signup_ts: datetime | None = None
    friends: list[int]  = []

external_data = {
    "id": "123",
    "signup_ts": "2026-02-25 15:14",
    "friends": [1, "2", "3"],
}

user = User(**external_data)
print(user)
print(user.friends)

# print(get_full_name("jane", "doe"))
# print(get_name_with_age("Jane", 16))