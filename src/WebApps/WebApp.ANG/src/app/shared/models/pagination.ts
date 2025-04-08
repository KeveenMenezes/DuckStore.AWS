export type Pagination<T> = {
  pageIndex: number;
  pageSize: number;
  totalItems: number;
  items: T[];
};
