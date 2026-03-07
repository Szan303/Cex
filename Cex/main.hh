import Math

class Program:

    private static void Test():
        for i = 0 to 10:
            if i == 5:
                break
            print "${i}"

    private static int Factorial(int n):
        if n <= 1:
            return 1
        return n * Factorial(n - 1)

    int Add(int o):
        o += 5
        o = Math.sqrt(2)
        return o

    public static void Main():
        int x = 1
        int y = 2
        checkpoint a
        if x > 100:
            float z = Add(x)
            print "wynik=${z}"
            print "|"
            Test()

            float res = Add(x)
            print "value of Add: " + res

            int result = Factorial(x)
            print result
        else:
            print "${x}. super" + x
            x++
            x++
            print "erouhgiw" + x
            goto a