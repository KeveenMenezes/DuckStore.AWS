export type Pagination<T> = {
  count: number;
  pageIndex: number;
  pageSize: number;
  totalItems: number;
  items: T[];
  pageCount: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
  isFirstPage: boolean;
  isLastPage: boolean;
  firstItemOnPage: number;
  lastItemOnPage: number;
};
