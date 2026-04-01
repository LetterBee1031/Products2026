str = input()
num = int(str)
out = num
for i in range(num):
    print(out, end="")
    if (out > 1):
        print(",", end="")
    out -= 1