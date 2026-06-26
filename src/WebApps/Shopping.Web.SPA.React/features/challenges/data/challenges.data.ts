import type { Challenge } from "@/features/challenges/types/challenge.types"

export const challenges: Challenge[] = [
  {
    id: "py-001",
    title: "Reversed List",
    description: "This Python code tries to reverse a list, but it has a bug. Find the error!",
    difficulty: "easy",
    language: "python",
    code: `def reverse_list(lst):
    reversed_lst = []
    for i in range(len(lst)):
        reversed_lst.append(lst[i])
    return reversed_lst

# Expected: [5, 4, 3, 2, 1]
# Result: [1, 2, 3, 4, 5]
print(reverse_list([1, 2, 3, 4, 5]))`,
    options: [
      "Replace lst[i] with lst[len(lst) - 1 - i]",
      "Replace range(len(lst)) with range(len(lst) - 1, -1, -1)",
      "Add reversed_lst.reverse() before the return",
      "Change append to insert(0, lst[i])"
    ],
    correctAnswer: 0,
    explanation: "The bug is in the index used to access the elements. By using lst[i], it copies in the same order. The fix is to use lst[len(lst) - 1 - i] to access from the end to the beginning, effectively reversing the list.",
    hints: [
      "Notice the order in which the elements are being accessed...",
      "The index i goes from 0 to len(lst)-1. In what order do you need to access to reverse?",
      "If the list has 5 elements, to reverse it you need to start from index 4 and go to 0."
    ],
    points: 100
  },
  {
    id: "js-001",
    title: "Closure Trap",
    description: "This JavaScript loop should print 0, 1, 2, but it prints 3, 3, 3. Why?",
    difficulty: "medium",
    language: "javascript",
    code: `for (var i = 0; i < 3; i++) {
  setTimeout(function() {
    console.log(i);
  }, 1000);
}
// Expected: 0, 1, 2
// Result: 3, 3, 3`,
    options: [
      "Replace var with let",
      "Add i = 0 inside the setTimeout",
      "Replace setTimeout with setInterval",
      "Remove the anonymous function"
    ],
    correctAnswer: 0,
    explanation: "The classic closure problem with var! The var variable is function-scoped, not block-scoped. When the setTimeout callbacks run, the loop has already finished and i is 3. Replacing var with let creates a block scope for each iteration, preserving the correct value of i.",
    hints: [
      "Think about when the setTimeout actually runs the callback...",
      "What is the scope difference between var and let?",
      "var is function-scoped. When the callbacks run, the loop is already over."
    ],
    points: 200
  },
  {
    id: "ts-001",
    title: "Wrong Generic Type",
    description: "This TypeScript function has a type error. Find the problem!",
    difficulty: "medium",
    language: "typescript",
    code: `interface User {
  name: string;
  age: number;
}

function getProperty<T>(obj: T, key: string) {
  return obj[key]; // Error!
}

const user: User = { name: "Ana", age: 25 };
const name = getProperty(user, "name");`,
    options: [
      "Type key as keyof T instead of string",
      "Add as any after obj[key]",
      "Replace interface with type",
      "Remove the generic T"
    ],
    correctAnswer: 0,
    explanation: "TypeScript cannot guarantee that the string passed as key actually exists in T. The solution is to use keyof T to constrain the key parameter to only the valid keys of the object, making the code type-safe.",
    hints: [
      "TypeScript needs to know that the key exists in the object...",
      "There is an operator in TypeScript that extracts the keys of a type...",
      "keyof T returns a union of all the keys of T."
    ],
    points: 200
  },
  {
    id: "java-001",
    title: "String Comparison",
    description: "This Java code compares strings the wrong way. What is the problem?",
    difficulty: "easy",
    language: "java",
    code: `public class Main {
    public static void main(String[] args) {
        String a = new String("hello");
        String b = new String("hello");

        if (a == b) {
            System.out.println("Equal!");
        } else {
            System.out.println("Different!");
        }
        // Prints: "Different!"
        // Expected: "Equal!"
    }
}`,
    options: [
      "Replace == with .equals()",
      "Use String.compare(a, b)",
      "Replace new String() with direct assignment",
      "Add .toString() before the comparison"
    ],
    correctAnswer: 0,
    explanation: "In Java, the == operator compares references (memory addresses), not the content of the strings. Since a and b are different objects created with new, == returns false. The .equals() method compares the content of the strings, which is the desired behavior.",
    hints: [
      "In Java, == works differently for objects and primitives...",
      "When you use new, each call creates a different object in memory...",
      "To compare the content of objects in Java, use a specific method."
    ],
    points: 100
  },
  {
    id: "cs-001",
    title: "Null Reference Exception",
    description: "This C# code throws a NullReferenceException. Where is the error?",
    difficulty: "medium",
    language: "csharp",
    code: `public class UserService
{
    private List<string> _users;

    public void AddUser(string name)
    {
        _users.Add(name); // NullReferenceException!
    }

    public int GetCount()
    {
        return _users.Count;
    }
}`,
    options: [
      "Initialize _users = new List<string>() at the declaration",
      "Add a null check before _users.Add()",
      "Replace List<string> with string[]",
      "Add static to the _users field"
    ],
    correctAnswer: 0,
    explanation: "The _users field was declared but never initialized, so its default value is null. Trying to call .Add() on null causes a NullReferenceException. The solution is to initialize the list at the declaration: private List<string> _users = new List<string>();",
    hints: [
      "What is the default value of a reference field in C#?",
      "Can you call methods on an object that is null?",
      "The list needs to exist before you add items to it."
    ],
    points: 200
  },
  {
    id: "cpp-001",
    title: "Silent Overflow",
    description: "This C++ code has an overflow bug. Identify the problem!",
    difficulty: "hard",
    language: "cpp",
    code: `#include <iostream>
using namespace std;

int factorial(int n) {
    int result = 1;
    for (int i = 1; i <= n; i++) {
        result *= i;
    }
    return result;
}

int main() {
    cout << factorial(20) << endl;
    // Result: -2102132736
    // Expected: 2432902008176640000
    return 0;
}`,
    options: [
      "Replace int with long long in result and the return type",
      "Add unsigned before int",
      "Use double instead of int",
      "Add an overflow check in the loop"
    ],
    correctAnswer: 0,
    explanation: "The int type in C++ is typically 32 bits, with a maximum value of ~2.1 billion. The factorial of 20 is ~2.4 quintillion, far beyond that limit. Using long long (64 bits), the maximum value is ~9.2 quintillion, enough for factorial(20).",
    hints: [
      "What is the maximum value an int can store in C++?",
      "20! is a VERY large number. How large?",
      "C++ has integer types larger than int..."
    ],
    points: 300
  },
  {
    id: "sql-001",
    title: "Incorrect JOIN",
    description: "This SQL query returns more rows than expected. What is the problem?",
    difficulty: "medium",
    language: "sql",
    code: `-- Tables: users (id, name), orders (id, user_id, total)
-- We want: total spent per user

SELECT u.name, SUM(o.total) as total_spent
FROM users u, orders o
WHERE u.id = o.user_id;

-- Missing GROUP BY! Incorrect result.`,
    options: [
      "Add GROUP BY u.name at the end of the query",
      "Replace SUM with COUNT",
      "Add DISTINCT before u.name",
      "Replace WHERE with HAVING"
    ],
    correctAnswer: 0,
    explanation: "When we use aggregate functions like SUM() together with non-aggregated columns, we need GROUP BY to indicate how to group the results. Without GROUP BY, the database tries to aggregate everything into a single row or returns an error, depending on the DBMS.",
    hints: [
      "Aggregate functions like SUM() need to know HOW to group...",
      "You want the total PER user, so you need to group...",
      "GROUP BY defines which columns determine the aggregation groups."
    ],
    points: 200
  },
  {
    id: "py-002",
    title: "Mutable Default",
    description: "This Python function has unexpected behavior with the default argument. Figure it out!",
    difficulty: "hard",
    language: "python",
    code: `def add_item(item, lst=[]):
    lst.append(item)
    return lst

# Calls:
print(add_item("a"))  # ['a'] - OK
print(add_item("b"))  # ['a', 'b'] - Bug!
print(add_item("c"))  # ['a', 'b', 'c'] - Bug!`,
    options: [
      "Use None as the default and create the list inside the function",
      "Replace lst=[] with lst=list()",
      "Add lst.clear() at the start",
      "Use *args instead of lst"
    ],
    correctAnswer: 0,
    explanation: "In Python, mutable default arguments (like lists) are evaluated only ONCE when the function is defined, not on each call. The same list is shared across all calls. The idiomatic solution is to use None as the default and create the list inside the function: if lst is None: lst = []",
    hints: [
      "When exactly does Python evaluate the default values of arguments?",
      "Mutable objects like lists can be modified in-place...",
      "The default is created once and reused across all calls."
    ],
    points: 300
  },
  {
    id: "js-002",
    title: "Lost Promise",
    description: "This async function doesn't wait for the result correctly. Where is the bug?",
    difficulty: "hard",
    language: "javascript",
    code: `async function fetchUsers() {
  const userIds = [1, 2, 3, 4, 5];
  const users = [];

  userIds.forEach(async (id) => {
    const response = await fetch(\`/api/users/\${id}\`);
    const user = await response.json();
    users.push(user);
  });

  console.log(users); // [] - Empty array!
  return users;
}`,
    options: [
      "Replace forEach with for...of or use Promise.all with map",
      "Add await before userIds.forEach",
      "Replace const users with let users",
      "Move the console.log inside the forEach"
    ],
    correctAnswer: 0,
    explanation: "forEach does not wait for async callbacks! It fires off all the Promises but doesn't await any of them. The console.log runs before any fetch finishes. The solution is to use for...of (sequential) or Promise.all(userIds.map(...)) (parallel) to ensure all the Promises are resolved.",
    hints: [
      "Does forEach know how to handle async functions?",
      "When does the console.log run relative to the fetches?",
      "There are better ways to iterate with async/await..."
    ],
    points: 300
  }
]
