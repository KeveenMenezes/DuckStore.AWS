import type { Product } from "@/features/products/types/product.types"

export const products: Product[] = [
  {
    id: "duck-debug-classic",
    name: "Debug Duck Classic",
    description: "The classic rubber duck for debugging. Your most loyal coding companion.",
    longDescription: "The Debug Duck Classic is the perfect companion for every programmer. Based on the famous Rubber Duck Debugging technique, this premium rubber duck is ready to listen patiently while you explain your code line by line. Made with high-quality material and a friendly smile that inspires confidence.",
    price: 29.90,
    image: "/images/duck-hero.jpg",
    category: "classics",
    tags: ["debugging", "classic", "beginner"],
    stock: 50,
    rating: 4.8,
    reviews: 234
  },
  {
    id: "duck-python",
    name: "Python Duck",
    description: "Duck with a Python snake skin. Ideal for devs who love indentation.",
    longDescription: "The Python Duck comes decorated with the iconic colors of the Python language. With subtle scales and the Python logo printed on it, it's the ideal partner for debugging that indentation error driving you crazy. Includes a mini Zen of Python manual.",
    price: 39.90,
    image: "/images/duck-hero.jpg",
    category: "languages",
    tags: ["python", "language", "backend"],
    stock: 30,
    rating: 4.9,
    reviews: 187
  },
  {
    id: "duck-javascript",
    name: "JavaScript Duck",
    description: "Vibrant yellow duck with the JS logo. For those who live in console.log().",
    longDescription: "The JavaScript Duck is as dynamic as the language it represents. With its vibrant yellow and the iconic JS logo, it's the perfect companion for when 'undefined is not a function' shows up in your console. Bonus: it will never return NaN.",
    price: 39.90,
    image: "/images/duck-hero.jpg",
    category: "languages",
    tags: ["javascript", "language", "frontend", "web"],
    stock: 45,
    rating: 4.7,
    reviews: 312
  },
  {
    id: "duck-fullstack",
    name: "Full Stack Duck",
    description: "Premium duck with layers representing frontend, backend and database.",
    longDescription: "The Full Stack Duck is a triple duck! Each layer represents a part of the stack: the head is the frontend (pretty and visible), the body is the backend (where the magic happens) and the base is the database (solid and stable). Perfect for those who work on everything.",
    price: 59.90,
    image: "/images/duck-hero.jpg",
    category: "specials",
    tags: ["fullstack", "premium", "advanced"],
    stock: 15,
    rating: 5.0,
    reviews: 89
  },
  {
    id: "duck-devops",
    name: "DevOps Duck",
    description: "Duck with a construction helmet and Docker logo. Deploy without fear!",
    longDescription: "The DevOps Duck comes equipped with a mini construction helmet and a toy Docker container. It's ready to help you debug CI/CD pipelines, resolve merge conflicts and make sure the deploy goes to production without issues. Includes an 'It works on my machine' sticker.",
    price: 49.90,
    image: "/images/duck-hero.jpg",
    category: "specials",
    tags: ["devops", "docker", "deploy", "ci-cd"],
    stock: 25,
    rating: 4.6,
    reviews: 156
  },
  {
    id: "duck-typescript",
    name: "TypeScript Duck",
    description: "A typed and safe duck. A guarantee of zero any in your code.",
    longDescription: "The TypeScript Duck is the typed and safe version of our beloved Debug Duck. With the iconic TypeScript blue and the expression of someone who takes types seriously, it will help you eliminate every 'any' from your code. Includes an advanced types guide.",
    price: 44.90,
    image: "/images/duck-hero.jpg",
    category: "languages",
    tags: ["typescript", "language", "types", "safe"],
    stock: 35,
    rating: 4.9,
    reviews: 201
  },
  {
    id: "duck-react",
    name: "React Duck",
    description: "Duck with a spinning propeller on its hat. An infinite re-render of cuteness!",
    longDescription: "The React Duck has a stylized propeller shaped like the React logo that actually spins! Ideal for debugging that useEffect with the wrong dependencies or understanding why your component is re-rendering infinitely. Comes with hooks stickers.",
    price: 44.90,
    image: "/images/duck-hero.jpg",
    category: "frameworks",
    tags: ["react", "framework", "frontend", "hooks"],
    stock: 40,
    rating: 4.8,
    reviews: 278
  },
  {
    id: "duck-sql",
    name: "SQL Duck",
    description: "Duck with a relational database on its chest. SELECT * FROM ducks.",
    longDescription: "The SQL Duck is the most organized duck in the collection. With a mini ER diagram on its chest and the expression of someone who knows how to write a perfect JOIN, it's ideal for when your queries are returning unexpected results. It will never give you a deadlock.",
    price: 34.90,
    image: "/images/duck-hero.jpg",
    category: "languages",
    tags: ["sql", "database", "queries"],
    stock: 20,
    rating: 4.5,
    reviews: 143
  }
]
