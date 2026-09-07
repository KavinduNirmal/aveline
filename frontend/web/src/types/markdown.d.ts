// Type declaration for raw markdown imports in Vite
declare module '*.md?raw' {
  const content: string
  export default content
}
