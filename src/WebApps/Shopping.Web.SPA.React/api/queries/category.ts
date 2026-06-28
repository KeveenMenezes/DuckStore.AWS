export const GET_CATEGORIES = `
  query GetCategories($pageSize: Int) {
    categories(pageSize: $pageSize) {
      items { id name parentId path }
    }
  }
`
